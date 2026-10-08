using System.Security.Claims;
using AcademiaAuditiva.Controllers;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AcademiaAuditiva.UnitTests;

public class AudioControllerTests
{
    private const string UserId = "student-1";
    private const string Token = "round-token";
    private const string MixedContainer = "piano-audio-mixed";
    private const string MixName = "mix-abc.wav";

    private readonly Mock<IAudioTokenService> _tokens = new();
    private readonly Mock<BlobServiceClient> _blobService = new();

    private AudioController CreateController() => new(_blobService.Object, _tokens.Object, NullLogger<AudioController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], "test"))
            }
        }
    };

    private void TokenResolvesTo(string? address) =>
        _tokens.Setup(t => t.ResolveTokenAsync(UserId, Token, It.IsAny<CancellationToken>())).ReturnsAsync(address);

    private void StoreBlob(string container, string name, byte[] content, long? contentLength = null, string contentType = "audio/wav")
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobProperties(contentType: contentType), Mock.Of<Response>()));
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Response.FromValue(
                BlobsModelFactory.BlobDownloadStreamingResult(
                    new MemoryStream(content),
                    BlobsModelFactory.BlobDownloadDetails(contentLength: contentLength ?? content.Length)),
                Mock.Of<Response>()));
        var containerClient = new Mock<BlobContainerClient>();
        containerClient.Setup(c => c.GetBlobClient(name)).Returns(blob.Object);
        _blobService.Setup(s => s.GetBlobContainerClient(container)).Returns(containerClient.Object);
    }

    [Fact]
    public async Task StreamByToken_DeliversTheSameClipAsDifferentBytesEveryTime()
    {
        var clip = TestWav.Pcm16(channels: 1, sampleRate: 8_000, [0, 500, -500, 1000, -1000, 0]);
        TokenResolvesTo($"{MixedContainer}/{MixName}");
        StoreBlob(MixedContainer, MixName, clip);
        var controller = CreateController();

        var first = (await controller.StreamByToken(Token, CancellationToken.None)).Should().BeOfType<FileContentResult>().Subject;
        var second = (await controller.StreamByToken(Token, CancellationToken.None)).Should().BeOfType<FileContentResult>().Subject;

        first.FileContents.Should().NotEqual(second.FileContents);
        foreach (var file in new[] { first, second })
        {
            file.ContentType.Should().Be("audio/wav");
            file.EnableRangeProcessing.Should().BeFalse("every response is unique");
            TestWav.ReadSamples(file.FileContents).Should().HaveCountGreaterThanOrEqualTo(6);
        }
        controller.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    // A round of the audio track compares the levels of its clips, which a random
    // gain per clip would change; the rest of the variation still makes them unique.
    [Fact]
    public async Task StreamByToken_KeepsTheLevelOfAProcessedClip()
    {
        short[] samples = [.. Enumerable.Repeat<short[]>([20000, -20000, 15000, -15000, 10000, 5000], 10).SelectMany(s => s)];
        const string processed = "proc-abc.wav";
        TokenResolvesTo($"{MixedContainer}/{processed}");
        StoreBlob(MixedContainer, processed, TestWav.Pcm16(channels: 1, sampleRate: 8_000, samples));
        var controller = CreateController();

        var responses = new List<byte[]>();
        for (var i = 0; i < 5; i++)
        {
            var file = (await controller.StreamByToken(Token, CancellationToken.None)).Should().BeOfType<FileContentResult>().Subject;
            responses.Add(file.FileContents);
            var played = TestWav.ReadSamples(file.FileContents);
            var start = Array.FindIndex(played, s => Math.Abs(s) > 1000);
            played.Skip(start).Take(samples.Length).Zip(samples, (p, s) => Math.Abs(p - s)).Max()
                .Should().BeLessThanOrEqualTo(1, "only the dither changes a sample");
        }
        responses.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task StreamByToken_StillVariesTheLevelOfAMix()
    {
        short[] samples = [.. Enumerable.Repeat<short>(20000, 60)];
        TokenResolvesTo($"{MixedContainer}/{MixName}");
        StoreBlob(MixedContainer, MixName, TestWav.Pcm16(channels: 1, sampleRate: 8_000, samples));
        var controller = CreateController();

        var peaks = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var file = (await controller.StreamByToken(Token, CancellationToken.None)).Should().BeOfType<FileContentResult>().Subject;
            peaks.Add(TestWav.ReadSamples(file.FileContents).Max(s => Math.Abs((int)s)));
        }

        peaks.Should().OnlyContain(p => p >= 20000 * ClipVariation.MinGain - 2 && p <= 20001);
        peaks.Should().Contain(p => p < 19990, "a mix is streamed at a random gain");
    }

    [Theory]
    [InlineData("piano-audio/C4.mp3")]
    [InlineData("other-container/mix-abc.wav")]
    [InlineData("other-container/proc-abc.wav")]
    public async Task StreamByToken_OnlyServesMixedClips(string address)
    {
        TokenResolvesTo(address);

        var result = await CreateController().StreamByToken(Token, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        _blobService.Verify(s => s.GetBlobContainerClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StreamByToken_UnknownTokenIsNotFound()
    {
        TokenResolvesTo(null);

        (await CreateController().StreamByToken(Token, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task StreamByToken_RefusesAClipThatIsNotPcm16Wav()
    {
        TokenResolvesTo($"{MixedContainer}/{MixName}");
        StoreBlob(MixedContainer, MixName, [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 0, 0xFF, 0xFB, 0x90, 0x00]);

        (await CreateController().StreamByToken(Token, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task StreamByToken_RefusesAnImplausiblyLargeClip()
    {
        TokenResolvesTo($"{MixedContainer}/{MixName}");
        StoreBlob(MixedContainer, MixName, TestWav.Pcm16(1, 8_000, [0, 0]), contentLength: 64L * 1024 * 1024);

        (await CreateController().StreamByToken(Token, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task StreamByName_KeepsTheAdminOnlySampleOutOfSharedCaches()
    {
        StoreBlob("piano-audio", "C4.mp3", [0xFF, 0xFB, 0x90, 0x00], contentType: "audio/mpeg");
        var controller = CreateController();

        var result = await controller.StreamByName("C4.mp3", CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>().Which.ContentType.Should().Be("audio/mpeg");
        controller.Response.Headers.CacheControl.ToString().Should().StartWith("private").And.NotContain("public");
    }
}
