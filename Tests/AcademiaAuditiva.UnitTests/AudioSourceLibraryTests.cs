using AcademiaAuditiva.Services.Audio.Sources;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The mixer reads a source by its catalog key, never by a path, and only as it was
/// measured; the readiness probe keeps an image without the right files from getting traffic.
/// </summary>
public class AudioSourceLibraryTests
{
    private static readonly AudioSourceLibrary Library = new(TempAudioSources.BundledRoot);

    [Fact]
    public async Task ASource_IsReadByItsName()
    {
        var source = Library.Resolve(AudioSourceLibrary.SampleName("pink-noise"));

        var audio = await Library.ReadAsync(source);

        source.Key.Should().Be("pink-noise");
        audio.SampleRate.Should().Be(source.Audio.SampleRate);
        audio.Channels.Should().Be(source.Audio.Channels);
        audio.Frames.Should().Be(source.Audio.Frames);
    }

    [Theory]
    [InlineData("pink-noise")]
    [InlineData("source:")]
    [InlineData("source:unknown")]
    [InlineData("source:Pink-Noise")]
    [InlineData("source:../appsettings")]
    [InlineData("source:pink-noise.wav")]
    public void OnlyListedSources_Resolve(string sampleName)
    {
        FluentActions.Invoking(() => Library.Resolve(sampleName)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AChangedFile_IsNotRead_AndMakesTheAppUnready()
    {
        using var folder = new TempAudioSources();
        var library = new AudioSourceLibrary(folder.Root);
        var bytes = await File.ReadAllBytesAsync(folder.PathOf("bass-line"));
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(folder.PathOf("bass-line"), bytes);

        await FluentActions.Awaiting(() => library.ReadAsync(library.Find("bass-line")!))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*'bass-line' changed*");
        library.Problems().Should().Equal("bass-line: bass-line.wav changed since it was measured");
        (await new AudioSourcesHealthCheck(library).CheckHealthAsync(new HealthCheckContext()))
            .Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task AMissingFile_MakesTheAppUnready()
    {
        using var folder = new TempAudioSources();
        var library = new AudioSourceLibrary(folder.Root);
        File.Delete(folder.PathOf("full-mix"));

        var result = await new AudioSourcesHealthCheck(library).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(
            "1 audio source problems were found, such as full-mix: full-mix.wav is missing.");
    }

    [Fact]
    public async Task TheBundledSources_MakeTheAppReady()
    {
        (await new AudioSourcesHealthCheck(Library).CheckHealthAsync(new HealthCheckContext()))
            .Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void ABrokenCatalog_StopsTheAppFromStarting()
    {
        using var folder = new TempAudioSources();
        folder.WriteCatalog(folder.Catalog().Select(s => s with { Audio = s.Audio with { SampleRate = 48000 } }));

        FluentActions.Invoking(() => new AudioSourceLibrary(folder.Root))
            .Should().Throw<InvalidDataException>().WithMessage("*48000 Hz*");
    }

    [Theory]
    [InlineData(
        "chunks/pixabay-484622-fe69012eef59-w01-stereo-4702345f3bb0.wav",
        "pixabay-484622-fe69012eef59-w01-stereo",
        2,
        "level",
        "eq")]
    [InlineData(
        "chunks/pixabay-484622-fe69012eef59-w01-mono-48df51886a7d.wav",
        "pixabay-484622-fe69012eef59-w01-mono",
        1,
        "pan",
        null)]
    public void PreparedStorageChunks_BecomePrivateCatalogSources(
        string blobName,
        string key,
        int channels,
        string firstUse,
        string? secondUse)
    {
        var hash = blobName.Split('-')[^1][..12] + new string('0', 52);
        var source = AudioSourceLibrary.FromStorageBlob(blobName,
            new AudioSourceMeasurement(44100, channels, 441000, -6, -23, hash));

        source.Should().NotBeNull();
        source!.Key.Should().Be(key);
        source.BlobName.Should().Be(blobName);
        source.Audio.Channels.Should().Be(channels);
        source.Uses.Should().Contain(firstUse);
        if (secondUse is not null)
            source.Uses.Should().Contain(secondUse);
    }

    [Theory]
    [InlineData("chunks/readme.txt")]
    [InlineData("masters/pixabay-484622.mp3")]
    [InlineData("chunks/pixabay-484622-w01-stereo-not-a-hash.wav")]
    public void NonPreparedStorageBlobs_AreIgnored(string blobName)
    {
        AudioSourceLibrary.FromStorageBlob(blobName,
            new AudioSourceMeasurement(44100, 2, 441000, -6, -23, new string('0', 64))).Should().BeNull();
    }

    [Theory]
    [InlineData("pixabay-550792-5462037249c8-w03-stereo", 1, false)]
    [InlineData("pixabay-550792-5462037249c8-w03-stereo", 2, true)]
    [InlineData("pixabay-550792-5462037249c8-w01-stereo", 2, false)]
    [InlineData("pixabay-554494-dd712bb896f7-w03-stereo", 3, false)]
    [InlineData("pixabay-554494-dd712bb896f7-w02-stereo", 3, true)]
    public void SpecificEligibilityExceptions_TakePrecedenceOverRecordingRules(string key, int difficulty, bool eligible)
    {
        AudioSourceLibrary.SupportsStorageEqDifficulty(key, difficulty).Should().Be(eligible);
    }

    [Fact]
    public void UnknownRecordings_AreNotPlayable()
    {
        AudioSourceLibrary.FromStorageBlob("chunks/pixabay-123-abcdefabcdef-w01-stereo-abcdefabcdef.wav",
            new AudioSourceMeasurement(44100, 2, 441000, -6, -23, "abcdefabcdef" + new string('0', 52)))
            .Should().BeNull();
    }
}
