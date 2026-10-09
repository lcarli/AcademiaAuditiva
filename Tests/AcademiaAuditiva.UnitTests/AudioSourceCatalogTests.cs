using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The recordings the app ships with: each one listed, licensed and measured, the files
/// exactly those measured, and enough of them for every kind of exercise and difficulty.
/// </summary>
public class AudioSourceCatalogTests
{
    private static readonly AudioSourceLibrary Library = new(TempAudioSources.BundledRoot);

    [Fact]
    public void EveryFile_IsTheOneMeasured()
    {
        Library.Sources.Should().NotBeEmpty();
        foreach (var source in Library.Sources)
        {
            var measured = AudioSourceMeasurement.Of(File.ReadAllBytes(Library.PathOf(source)));

            measured.Should().BeEquivalentTo(source.Audio, options => options
                .Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 0.01)).WhenTypeIs<double>(),
                source.Key);
        }
        Library.Problems().Should().BeEmpty();
    }

    [Fact]
    public void TheFolder_HoldsOnlyTheListedRecordings()
    {
        var expected = Library.Sources.Select(s => s.FileName).Append(AudioSourceCatalog.FileName).Append("LICENSE.txt");

        Directory.GetFiles(TempAudioSources.BundledRoot).Select(Path.GetFileName).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void TheCatalog_IsWrittenAsTheScriptWritesIt()
    {
        var text = File.ReadAllText(Path.Combine(TempAudioSources.BundledRoot, AudioSourceCatalog.FileName)).ReplaceLineEndings("\n");

        AudioSourceCatalog.Serialize(Library.Sources).Should().Be(text, "scripts/audio-sources.cs measure rewrites it");
    }

    [Fact]
    public void EveryUse_HasSourcesAtEveryDifficulty()
    {
        foreach (var use in AudioSourceRules.Uses)
        {
            for (var difficulty = 1; difficulty <= 3; difficulty++)
            {
                Library.Sources.Should().Contain(s => s.Uses.Contains(use) && s.Difficulties.Contains(difficulty), $"{use} at {difficulty}");
            }
        }
        Library.Sources.Should().Contain(s => s.Audio.Channels == 1).And.Contain(s => s.Audio.Channels == 2);
    }

    [Fact]
    public void GeneratedSources_AreTheRepositorys()
    {
        foreach (var source in Library.Sources.Where(s => s.Origin == AudioSourceRules.Generated))
        {
            source.License.Should().Be(new AudioSourceLicense("MIT", "AcademiaAuditiva contributors", "https://opensource.org/license/mit"));
        }
    }

    // The sources are made level, with room to be turned up before the mix scales them down.
    [Fact]
    public void Sources_AreEquallyLoud_WithHeadroom()
    {
        Library.Sources.Should().AllSatisfy(s =>
        {
            s.Audio.LoudnessLufs.Should().BeApproximately(-23, 0.1, s.Key);
            s.Audio.PeakDbfs.Should().BeLessThanOrEqualTo(-1.5, s.Key);
        });
    }

    public static TheoryData<string, string> Malformed => new()
    {
        { "{", "malformed" },
        { """{ "sources": [], "extra": 1 }""", "malformed" },
        { "{}", "no sources" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedCatalog_IsRefused(string json, string reason)
    {
        FluentActions.Invoking(() => AudioSourceCatalog.Parse(json))
            .Should().Throw<InvalidDataException>().WithMessage($"*{reason}*");
    }

    [Fact]
    public void ACatalogBreakingTheRules_IsRefused_NamingEveryProblem()
    {
        var first = Library.Sources[0];
        var json = AudioSourceCatalog.Serialize([first, first, Library.Sources[1] with { Kind = "cowbell" }]);

        FluentActions.Invoking(() => AudioSourceCatalog.Parse(json))
            .Should().Throw<InvalidDataException>()
            .WithMessage($"*{first.Key}: listed 2 times*")
            .WithMessage($"*{Library.Sources[1].Key}: its kind 'cowbell'*");
    }
}
