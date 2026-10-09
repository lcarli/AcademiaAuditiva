using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every source states where it came from and under which license, and has a file the
/// exercises can work with; each broken rule is named, so a bad entry is easy to fix.
/// </summary>
public class AudioSourceRulesTests
{
    private static readonly AudioSourceMeasurement GoodAudio =
        new(44100, 1, 8 * 44100, -9.4, -23, new string('a', 64));

    private static readonly AudioSource Good = new(
        "kick-loop", "A kick drum.", "drums", ["drums", "kick"], ["level", "pan"], [1, 2], AudioSourceRules.External,
        new AudioSourceLicense("CC0-1.0", "Someone", "https://creativecommons.org/publicdomain/zero/1.0/", "https://example.org/kick.wav"),
        GoodAudio);

    [Fact]
    public void AGoodSource_HasNoProblems()
    {
        AudioSourceRules.Check(Good).Should().BeEmpty();
        AudioSourceRules.Check(Generated()).Should().BeEmpty();
    }

    public static TheoryData<string, AudioSource> Broken => new()
    {
        { "48000 Hz", Good with { Audio = GoodAudio with { SampleRate = 48000 } } },
        { "6 channels", Good with { Audio = GoodAudio with { Channels = 6 }, Uses = ["level"] } },
        { "lasts 1.00 s", Good with { Audio = GoodAudio with { Frames = 44100 } } },
        { "lasts 25.00 s", Good with { Audio = GoodAudio with { Frames = 25 * 44100 } } },
        { "clips", Good with { Audio = GoodAudio with { PeakDbfs = -0.5 } } },
        { "loudness is -Infinity", Good with { Audio = GoodAudio with { LoudnessLufs = double.NegativeInfinity } } },
        { "loudness is -40.00", Good with { Audio = GoodAudio with { LoudnessLufs = -40 } } },
        { "loudness is -10.00", Good with { Audio = GoodAudio with { LoudnessLufs = -10 } } },
        { "SHA-256", Good with { Audio = GoodAudio with { Sha256 = "ABC" } } },
        { "no measurements", Good with { Audio = null! } },
        { "key", Good with { Key = "../kick" } },
        { "key", Good with { Key = "Kick" } },
        { "description", Good with { Description = " " } },
        { "kind 'cowbell'", Good with { Kind = "cowbell" } },
        { "tags", Good with { Tags = [] } },
        { "tags", Good with { Tags = ["drums", "drums"] } },
        { "tags", Good with { Tags = ["loud"] } },
        { "uses", Good with { Uses = ["reverb"] } },
        { "only a mono source can be panned", Good with { Audio = GoodAudio with { Channels = 2 } } },
        { "difficulties", Good with { Difficulties = [0] } },
        { "difficulties", Good with { Difficulties = [] } },
        { "license", Good with { License = Good.License with { Author = "" } } },
        { "license", Good with { License = Good.License with { Url = "http://example.org" } } },
        { "obtained from", Good with { License = Good.License with { SourceUrl = null } } },
        { "CC-BY-4.0 asks for an attribution", Good with { License = Good.License with { Name = "CC-BY-4.0" } } },
        { "origin 'found'", Good with { Origin = "found" } },
        { "MIT, without a source URL", Generated() with { License = Good.License } },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void ABrokenRule_IsNamed(string problem, AudioSource source)
    {
        AudioSourceRules.Check(source).Should().ContainSingle()
            .Which.Should().StartWith($"{source.Key}: ").And.Contain(problem);
    }

    [Fact]
    public void AnAttributedCcBySource_IsFine()
    {
        var source = Good with { License = Good.License with { Name = "CC-BY-4.0", Attribution = "Kick by Someone (CC BY 4.0)" } };

        AudioSourceRules.Check(source).Should().BeEmpty();
    }

    private static AudioSource Generated() => Good with
    {
        Origin = AudioSourceRules.Generated,
        License = new AudioSourceLicense("MIT", "AcademiaAuditiva contributors", "https://opensource.org/license/mit"),
    };
}
