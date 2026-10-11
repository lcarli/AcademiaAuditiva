using System.Text.Json.Nodes;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>Opt-in verification against private prepared files, never copied into the repository.</summary>
public class PrivateTrainingSourcesTests
{
    [Fact]
    public void AnUnapprovedManifest_IsRejectedByRequestsAndReadiness()
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(client => client.DownloadContent(It.IsAny<CancellationToken>()))
            .Returns(Downloaded("{}"u8.ToArray()));
        var storage = new Mock<BlobContainerClient>();
        storage.Setup(client => client.GetBlobClient(AudioSourceLibrary.StorageManifestBlob)).Returns(blob.Object);
        var library = new AudioSourceLibrary(TempAudioSources.BundledRoot, storage.Object);
        library.Problems().Should().ContainSingle(problem => problem.Contains("approved SHA-256"));
        FluentActions.Invoking(() => library.Sources).Should().Throw<InvalidDataException>();
    }

    [PrivateAudioFact]
    public async Task ProductionStyleDiscovery_ReadsAll96ApprovedChunks_AndRetryRecoversFromAMissingBlob()
    {
        var (library, storage, sources) = Library();
        var available = sources.Skip(1).Select(source => BlobsModelFactory.BlobItem(source.BlobName)).ToArray();
        storage.Setup(client => client.GetBlobs(BlobTraits.None, BlobStates.None, "chunks/", It.IsAny<CancellationToken>()))
            .Returns(() => Pageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(available, null, Mock.Of<Response>())]));
        library.Problems().Should().ContainSingle(problem => problem.Contains("missing 1"));
        available = sources.Select(source => BlobsModelFactory.BlobItem(source.BlobName)).ToArray();
        library.Problems().Should().BeEmpty();
        library.Sources.Count(source => source.IsRemote).Should().Be(96);
        var health = await new AudioSourcesHealthCheck(library).CheckHealthAsync(new HealthCheckContext());
        health.Data["preparedChunks"].Should().Be(96);
        health.Data["privateStorage"].Should().Be(true);
        foreach (var source in sources)
        {
            AudioSourceRules.Check(source).Should().BeEmpty();
            var audio = await library.ReadAsync(source);
            audio.Channels.Should().Be(source.Audio.Channels);
            audio.Frames.Should().Be(441000);
        }
    }

    [PrivateAudioFact]
    public async Task FullHashesAndDecodedLayout_AreEnforced()
    {
        var (library, _, sources) = Library();
        var source = sources[0];
        var changedHash = source with { Audio = source.Audio with { Sha256 = source.Audio.Sha256[..12] + new string('0', 52) } };
        await FluentActions.Awaiting(() => library.ReadAsync(changedHash)).Should().ThrowAsync<InvalidDataException>();
        var changedLayout = source with { Audio = source.Audio with { Channels = 1 } };
        await FluentActions.Awaiting(() => library.ReadAsync(changedLayout)).Should().ThrowAsync<InvalidDataException>();

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "storage-manifest.json")))!;
        manifest["chunks"]![0]!["audio"]!["sha256"] = new string('0', 64);
        FluentActions.Invoking(() => AudioSourceLibrary.FromStorageManifest(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString())))
            .Should().Throw<InvalidDataException>();
    }

    [PrivateAudioFact]
    public async Task EveryPermittedPrivateVariant_IsMusicMatchedSafeAndMeasurablyChanged()
    {
        var (library, _, sources) = Library();
        var generator = new TechnicalListeningRoundGenerator(library, new NewAudioExerciseTests.SequenceRandom());
        foreach (var (level, difficulty, expectedCount) in new[]
        {
            ("beginner", 1, 30), ("intermediate", 2, 33), ("advanced", 3, 47),
        })
        {
            sources.Count(source => source.Audio.Channels == 2 && AudioSourceLibrary.SupportsStorageEqDifficulty(source.Key, difficulty))
                .Should().Be(expectedCount);
            foreach (var frequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
            {
                var eligible = generator.FrequencySourcesFor(level, frequency);
                eligible.Should().OnlyContain(source => source.IsRemote);
                foreach (var source in eligible)
                {
                    var input = await library.ReadAsync(source);
                    var gain = level == "beginner" ? 9 : 6;
                    var q = level == "advanced" ? 2 : 1;
                    CheckPair(input, new PeakingEqProcessor(frequency, gain, q), -26, source.Key);
                }
            }
        }
        var eqSources = generator.FrequencySourcesFor("intermediate", 1000);
        foreach (var source in eqSources)
        {
            var input = await library.ReadAsync(source);
            foreach (var frequency in new[] { 125, 1000, 8000 })
            foreach (var gain in new[] { -9, -6, -3, 3, 6, 9 })
                CheckPair(input, new PeakingEqProcessor(frequency, gain, 1), -28, source.Key);
            foreach (var frequency in new[] { 500, 1000, 2000 })
            foreach (var q in new[] { 0.35, 0.5, 0.7, 1, 1.4, 1.5, 2.8, 4 })
                CheckPair(input, new PeakingEqProcessor(frequency, 9, q), -28, source.Key);
        }
        foreach (var name in new[] { "LevelMatch", "StereoPosition", "GuessFrequency", "BoostOrCut", "GuessQ", "Compression" })
            generator.Plan(new Exercise { Name = name }, new Dictionary<string, string>())
                .Clips.Should().OnlyContain(clip => library.Find(clip.Plan.SourceKey)!.IsRemote);
    }

    [PrivateAudioFact]
    public async Task EveryOfferedFrequency_HasUsefulEnergyOnEveryEligibleMusicSource()
    {
        var (library, _, sources) = Library();
        foreach (var source in sources.Where(source => source.Audio.Channels == 2))
        {
            var input = await library.ReadAsync(source);
            var spectrum = GuessFrequencyTests.Spectrum(input, fullClip: true);
            var size = (spectrum.Length - 1) * 2;
            foreach (var (level, difficulty) in new[] { ("beginner", 1), ("intermediate", 2), ("advanced", 3) })
            {
                if (!AudioSourceLibrary.SupportsStorageEqDifficulty(source.Key, difficulty)) continue;
                foreach (var frequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
                {
                    var band = GuessFrequencyTests.BandBins(frequency, input.SampleRate, size);
                    (band.Sum(index => spectrum[index]) / spectrum.Sum()).Should().BeGreaterThanOrEqualTo(0.001,
                        $"{source.Key}, {level}, {frequency} Hz must contain useful third-octave energy");
                }
            }
        }
    }

    [PrivateAudioFact]
    public async Task EveryCompressionSource_ReducesTransientContrastAtMatchedLoudness()
    {
        var (library, _, sources) = Library();
        using var assertions = new FluentAssertions.Execution.AssertionScope();
        foreach (var source in sources.Where(source => AudioSourceLibrary.SupportsStorageCompression(source.Key)))
        {
            var input = await library.ReadAsync(source);
            foreach (var level in new[] { "beginner", "intermediate", "advanced" })
            {
                var output = CheckPair(input, TechnicalListeningRoundGenerator.CompressionFor(level), -28, source.Key);
                var crestReduction = Crest(input) - Crest(output);
                crestReduction.Should().BeGreaterThanOrEqualTo(level == "beginner" ? 3 : level == "intermediate" ? 2 : 1,
                    $"{source.Key}, {level} must reduce transient contrast");
            }
        }
    }

    private static PcmAudio CheckPair(PcmAudio input, AudioProcessor processor, double target, string source)
    {
        var reference = AudioProcessing.Process(input, [new LoudnessMatchProcessor(target)]);
        var processed = AudioProcessing.Process(input, [processor, new LoudnessMatchProcessor(target)]);
        foreach (var audio in new[] { reference, processed })
        {
            audio.Samples.All(sample => float.IsFinite(sample) && Math.Abs(sample) <= 0.98).Should().BeTrue(source);
            Loudness.IntegratedLufs(audio).Should().BeApproximately(target, 0.1, source);
        }
        NewAudioExerciseTests.RelativeDifference(reference, processed).Should().BeGreaterThan(0.015, source);
        return processed;
    }

    private static double Crest(PcmAudio audio) =>
        20 * Math.Log10(audio.Samples.Max(sample => Math.Abs(sample))
            / Math.Sqrt(audio.Samples.Average(sample => (double)sample * sample)));

    private static string Folder => Environment.GetEnvironmentVariable("AA_TEST_TRAINING_SOURCES")!;

    private static (AudioSourceLibrary Library, Mock<BlobContainerClient> Storage, IReadOnlyList<AudioSource> Sources) Library()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Folder, "storage-manifest.json"));
        var sources = AudioSourceLibrary.FromStorageManifest(bytes);
        var manifest = new Mock<BlobClient>();
        manifest.Setup(client => client.DownloadContent(It.IsAny<CancellationToken>())).Returns(Downloaded(bytes));
        var storage = new Mock<BlobContainerClient>();
        storage.Setup(client => client.GetBlobClient(AudioSourceLibrary.StorageManifestBlob)).Returns(manifest.Object);
        foreach (var source in sources)
        {
            var blob = new Mock<BlobClient>();
            blob.Setup(client => client.DownloadContentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Downloaded(File.ReadAllBytes(Path.Combine(Folder, source.FileName))));
            storage.Setup(client => client.GetBlobClient(source.BlobName!)).Returns(blob.Object);
        }
        storage.Setup(client => client.GetBlobs(BlobTraits.None, BlobStates.None, "chunks/", It.IsAny<CancellationToken>()))
            .Returns(Pageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(
                sources.Select(source => BlobsModelFactory.BlobItem(source.BlobName)).ToArray(), null, Mock.Of<Response>())]));
        return (new AudioSourceLibrary(TempAudioSources.BundledRoot, storage.Object), storage, sources);
    }

    private static Response<BlobDownloadResult> Downloaded(byte[] bytes) =>
        Response.FromValue(BlobsModelFactory.BlobDownloadResult(content: BinaryData.FromBytes(bytes)), Mock.Of<Response>());

    private sealed class PrivateAudioFactAttribute : FactAttribute
    {
        public PrivateAudioFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AA_TEST_TRAINING_SOURCES")))
                Skip = "Set AA_TEST_TRAINING_SOURCES to a folder containing the private prepared WAVs and storage manifest.";
        }
    }
}
