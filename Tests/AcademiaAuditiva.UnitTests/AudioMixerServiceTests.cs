using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Storage deletes a mixed clip about a day after its last write, while the app can
/// run for days. The mixer must therefore never hand out the name of a clip that may
/// be gone: it rechecks, and touches, any mix it has not written for half a day.
/// When it does mix, the samples of the other instruments come from the app itself.
/// </summary>
public class AudioMixerServiceTests
{
    private const string MixedContainer = "piano-audio-mixed";
    private const string FifthMix = "mix-5ef716d444d62c7197b41e1997f63d9b183699d6ad9bcf61edd5e1cadcd7bba7.wav";
    private const int SampleRate = 44100;

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
        _mixer = new AudioMixerService(
            blobService.Object, new BundledSamples(BundledSamplesTests.Root), _clock, NullLogger<AudioMixerService>.Instance);
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
        StorageAnswers(NotFound());

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

    [Fact]
    public async Task GuitarNote_IsReadFromTheApp_AndFadesOutInsteadOfClicking()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();

        await _mixer.MixAsync([new("guitar/C4.mp3", 0, 1.5)]);

        _downloads.Should().BeEmpty("the guitar samples ship with the app");
        var mix = Wav.Parse(uploaded());
        mix.SampleRate.Should().Be(SampleRate);
        mix.Channels.Should().Be(1);
        mix.Samples.Should().HaveCount((int)(1.5 * SampleRate));
        Peak(mix.Samples.Take(SampleRate / 2)).Should().BeGreaterThan(0.05f, "the note is heard");
        Peak(mix.Samples.TakeLast(10)).Should().BeLessThan(0.01f, "the note is cut short: it fades out over its last 25 ms");
    }

    [Fact]
    public async Task NotesAddingUpPastFullScale_AreScaledDown_InsteadOfClipped()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        await _mixer.MixAsync([new("violin/C5.mp3", 0, 1.5)]);
        var single = Wav.Parse(uploaded()).Samples;

        await _mixer.MixAsync([.. Enumerable.Repeat(new MixInput("violin/C5.mp3", 0, 1.5), 4)]);
        var loud = Wav.Parse(uploaded()).Samples;

        (Peak(single) * 4).Should().BeGreaterThan(0.98f, "four of these notes add up past full scale");
        Peak(loud).Should().BeApproximately(0.98f, 0.0005f);
        var scale = 0.98f / Peak(single);
        loud.Zip(single, (l, s) => Math.Abs(l - s * scale)).Max()
            .Should().BeLessThan(0.0005f, "the whole mix is scaled down, so the notes keep their shape");
        _downloads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("guitar/../../appsettings.json")]
    [InlineData("drums/C4.mp3")]
    public async Task OnlyInstrumentSamples_AreReadFromTheApp(string sampleName)
    {
        StorageAnswers(NotFound());

        await FluentActions.Awaiting(() => _mixer.MixAsync([new(sampleName, 0, 1)])).Should().ThrowAsync<ArgumentException>();

        _downloads.Should().BeEmpty();
    }

    private static RequestFailedException NotFound() =>
        new(404, "The specified blob does not exist.", "BlobNotFound", null);

    private static float Peak(IEnumerable<float> samples) => samples.Max(Math.Abs);

    /// <summary>Returns the last WAV the mixer uploaded.</summary>
    private Func<byte[]> CaptureUploads()
    {
        byte[]? uploaded = null;
        _mixedBlob.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(), It.IsAny<AccessTier?>(),
                It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .Callback((Stream content, BlobHttpHeaders _, IDictionary<string, string> _, BlobRequestConditions _,
                IProgress<long> _, AccessTier? _, StorageTransferOptions _, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                uploaded = copy.ToArray();
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        return () => uploaded ?? throw new InvalidOperationException("Nothing was uploaded.");
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

    /// <summary>The 16-bit PCM WAV the mixer writes, with its samples back in [-1, 1].</summary>
    private sealed record Wav(int SampleRate, int Channels, float[] Samples)
    {
        public static Wav Parse(byte[] bytes)
        {
            Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
            Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WAVE");
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)).Should().Be(16, "the mix is 16-bit PCM");
            Encoding.ASCII.GetString(bytes, 36, 4).Should().Be("data");
            var dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            dataSize.Should().Be(bytes.Length - 44);

            var samples = new float[dataSize / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + i * 2, 2)) / 32767f;
            }

            return new Wav(
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)),
                samples);
        }
    }
}
