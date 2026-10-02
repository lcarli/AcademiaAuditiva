using System.Globalization;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Storage deletes a mixed clip about a day after its last write, while the app can
/// run for days. The mixer must therefore never hand out the name of a clip that may
/// be gone: it rechecks, and touches, any mix it has not written for half a day.
/// </summary>
public class AudioMixerServiceTests
{
    private const string MixedContainer = "piano-audio-mixed";
    private const string FifthMix = "mix-9439502e4f803f0295133c785731098ec8c69ef42c8f53e0fa4d1e46db53e340.wav";

    private static readonly DateTimeOffset Start = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly MixInput[] Fifth = [new("C4.mp3", 0, 1.5), new("G4.mp3", 1.75, 2.0)];

    private readonly ManualClock _clock = new(Start);
    private readonly Mock<BlobContainerClient> _mixedContainer = new();
    private readonly Mock<BlobClient> _mixedBlob = new();
    private readonly List<string> _downloads = [];
    private readonly AudioMixerService _mixer;

    public AudioMixerServiceTests()
    {
        _mixedContainer.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(_mixedBlob.Object);
        _mixedBlob.Setup(b => b.SetMetadataAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobInfo(new ETag("\"1\""), Start), Mock.Of<Response>()));

        // Mixing starts by downloading the samples; stopping there shows a new mix was needed.
        var sourceContainer = new Mock<BlobContainerClient>();
        sourceContainer.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns((string name) =>
        {
            var sample = new Mock<BlobClient>();
            sample.Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback(() => _downloads.Add(name))
                .ThrowsAsync(new MixingStarted());
            return sample.Object;
        });

        var blobService = new Mock<BlobServiceClient>();
        blobService.Setup(s => s.GetBlobContainerClient(MixedContainer)).Returns(_mixedContainer.Object);
        blobService.Setup(s => s.GetBlobContainerClient("piano-audio")).Returns(sourceContainer.Object);
        _mixer = new AudioMixerService(blobService.Object, _clock, NullLogger<AudioMixerService>.Instance);
    }

    [Fact]
    public async Task FreshMix_IsReused_WithoutAskingStorageAgain()
    {
        StoredMixWrittenAt(Start - TimeSpan.FromHours(1));

        var first = await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(10));
        var second = await _mixer.MixAsync(Fifth);

        first.Should().Be(new MixedAudio(MixedContainer, FifthMix));
        second.Should().Be(first);
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyTouched(Times.Never());
        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task OldMix_IsTouched_SoStorageKeepsIt()
    {
        StoredMixWrittenAt(Start - TimeSpan.FromHours(13));

        var first = await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(1));
        var second = await _mixer.MixAsync(Fifth);

        second.Should().Be(first);
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyTouched(Times.Once(), at: Start);
        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task RememberedMix_IsCheckedAgain_HalfADayAfterItsLastWrite()
    {
        StoredMixWrittenAt(Start);

        await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(12));
        var later = await _mixer.MixAsync(Fifth);

        later.Should().Be(new MixedAudio(MixedContainer, FifthMix));
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        VerifyTouched(Times.Once(), at: Start + TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task DeletedMix_IsMixedAgain()
    {
        StorageAnswers(new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null));

        await FluentActions.Awaiting(() => _mixer.MixAsync(Fifth)).Should().ThrowAsync<MixingStarted>();

        _downloads.Should().Equal("C4.mp3");
        VerifyTouched(Times.Never());
    }

    [Fact]
    public async Task OtherStorageErrors_AreNotMistakenForADeletedMix()
    {
        StorageAnswers(new RequestFailedException(403, "This request is not authorized.", "AuthorizationFailure", null));

        (await FluentActions.Awaiting(() => _mixer.MixAsync(Fifth)).Should().ThrowAsync<RequestFailedException>())
            .Which.Status.Should().Be(403);

        _downloads.Should().BeEmpty();
    }

    // Before, a pt-BR or fr-CA request wrote "0,0000" into the plan and so stored
    // a second copy of the same mix under another name.
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public async Task MixName_DependsOnlyOnThePlan_NotOnTheLanguage(string culture)
    {
        StoredMixWrittenAt(Start);
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            (await _mixer.MixAsync(Fifth)).Should().Be(new MixedAudio(MixedContainer, FifthMix));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        _mixedContainer.Verify(c => c.GetBlobClient(FifthMix), Times.Once);
    }

    private void StoredMixWrittenAt(DateTimeOffset lastModified) =>
        _mixedBlob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobProperties(lastModified: lastModified), Mock.Of<Response>()));

    private void StorageAnswers(RequestFailedException error) =>
        _mixedBlob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

    private void VerifyTouched(Times times, DateTimeOffset? at = null) =>
        _mixedBlob.Verify(b => b.SetMetadataAsync(
            It.Is<IDictionary<string, string>>(m => at == null
                || (m.Count == 1
                    && m.ContainsKey("touched")
                    && m["touched"] == at.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()), times);

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MixingStarted : Exception;
}
