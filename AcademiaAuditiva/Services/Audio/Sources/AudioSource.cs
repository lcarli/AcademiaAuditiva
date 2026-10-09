using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// A training recording of the audio track (docs/Audio-Ear-Training.md), listed in
/// <c>Audio/Sources/sources.json</c> and stored next to it as <c>{Key}.wav</c>.
/// </summary>
/// <param name="Key">Its stable name, never a storage address.</param>
/// <param name="Kind">What it is (<see cref="AudioSourceRules.Kinds"/>).</param>
/// <param name="Uses">The processors it suits (<see cref="AudioSourceRules.Uses"/>).</param>
/// <param name="Difficulties">The difficulties it suits, from 1 (beginner) to 3 (advanced).</param>
/// <param name="Origin"><c>generated</c> by scripts/audio-sources.cs, or <c>external</c>.</param>
/// <param name="Audio">What its file measures, written by scripts/audio-sources.cs.</param>
public sealed record AudioSource(
    string Key,
    string Description,
    string Kind,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Uses,
    IReadOnlyList<int> Difficulties,
    string Origin,
    AudioSourceLicense License,
    AudioSourceMeasurement Audio)
{
    [JsonIgnore]
    public string FileName => $"{Key}.wav";

    /// <summary>Changes whenever its bytes do, so audio made from older bytes is never reused.</summary>
    [JsonIgnore]
    public string Version => Audio.Sha256;
}

/// <param name="Name">An SPDX-style license name, such as <c>MIT</c>, <c>CC0-1.0</c> or <c>CC-BY-4.0</c>.</param>
/// <param name="Url">Where the license text is.</param>
/// <param name="SourceUrl">Where an external recording was obtained.</param>
/// <param name="Attribution">The credit the license asks for, if any.</param>
public sealed record AudioSourceLicense(
    string Name,
    string Author,
    string Url,
    string? SourceUrl = null,
    string? Attribution = null);

/// <param name="Frames">Samples per channel.</param>
/// <param name="PeakDbfs">The sample peak, rounded to 0.01 dB.</param>
/// <param name="LoudnessLufs">The BS.1770 integrated loudness (<see cref="Loudness"/>), rounded to 0.01 LU.</param>
/// <param name="Sha256">The lowercase hex SHA-256 of the file.</param>
public sealed record AudioSourceMeasurement(
    int SampleRate,
    int Channels,
    int Frames,
    double PeakDbfs,
    double LoudnessLufs,
    string Sha256)
{
    [JsonIgnore]
    public double DurationSeconds => Frames / (double)SampleRate;

    /// <exception cref="InvalidDataException">The file is not a WAV file the sources can use.</exception>
    public static AudioSourceMeasurement Of(byte[] file)
    {
        var audio = WavFile.Read(file);
        return new AudioSourceMeasurement(
            audio.SampleRate,
            audio.Channels,
            audio.Frames,
            Math.Round(Loudness.SamplePeakDbfs(audio), 2),
            Math.Round(Loudness.IntegratedLufs(audio), 2),
            Convert.ToHexStringLower(SHA256.HashData(file)));
    }
}

/// <summary>What every source and its file must satisfy; each check names what is wrong.</summary>
public static partial class AudioSourceRules
{
    /// <summary>Every source has this rate, so any two of them can be mixed or compared.</summary>
    public const int SampleRate = 44100;

    public const double MinSeconds = 2;
    public const double MaxSeconds = 20;

    /// <summary>Headroom left for processing: a louder peak counts as clipped.</summary>
    public const double MaxPeakDbfs = -1;

    public const double MinLoudnessLufs = -36;
    public const double MaxLoudnessLufs = -14;

    public const string Generated = "generated";
    public const string External = "external";

    public static IReadOnlyList<string> Kinds { get; } = ["noise", "drums", "bass", "keys", "guitar", "vocal", "speech", "mix"];

    public static IReadOnlyList<string> Tags { get; } =
    [
        "noise", "broadband", "drums", "kick", "snare", "hihat", "percussion", "bass", "keys", "chords",
        "synth", "piano", "guitar", "vocal", "speech", "full-mix",
    ];

    /// <summary>The processors of the audio track; <c>pan</c> needs a mono source.</summary>
    public static IReadOnlyList<string> Uses { get; } = ["level", "pan", "eq"];

    /// <summary>The problems of a source's metadata and of its measured file.</summary>
    public static IEnumerable<string> Check(AudioSource source) =>
        CheckMetadata(source).Concat(source.Audio is null ? [] : CheckAudio(source.Audio)).Select(problem => $"{source.Key}: {problem}");

    /// <summary>The problems of a file, whatever its metadata.</summary>
    public static IEnumerable<string> CheckAudio(AudioSourceMeasurement audio) =>
        AudioProblems(audio).Select(problem => problem.ToString(CultureInfo.InvariantCulture));

    private static IEnumerable<FormattableString> AudioProblems(AudioSourceMeasurement audio)
    {
        if (audio.SampleRate != SampleRate)
            yield return $"its sample rate is {audio.SampleRate} Hz, not {SampleRate} Hz";
        if (audio.Channels is not (1 or 2))
            yield return $"it has {audio.Channels} channels, not 1 or 2";
        if (audio.DurationSeconds is < MinSeconds or > MaxSeconds)
            yield return $"it lasts {audio.DurationSeconds:F2} s, not {MinSeconds} to {MaxSeconds} s";
        if (audio.PeakDbfs > MaxPeakDbfs)
            yield return $"it peaks at {audio.PeakDbfs:F2} dBFS, above {MaxPeakDbfs} dBFS: it clips or leaves no headroom";
        if (!(audio.LoudnessLufs is >= MinLoudnessLufs and <= MaxLoudnessLufs))
            yield return $"its loudness is {audio.LoudnessLufs:F2} LUFS, not {MinLoudnessLufs} to {MaxLoudnessLufs} LUFS";
        if (!Sha256Pattern().IsMatch(audio.Sha256 ?? ""))
            yield return $"its SHA-256 is not 64 lowercase hex digits";
    }

    private static IEnumerable<string> CheckMetadata(AudioSource source)
    {
        if (!KeyPattern().IsMatch(source.Key ?? ""))
            yield return "its key is not lowercase letters, digits and dashes";
        if (string.IsNullOrWhiteSpace(source.Description))
            yield return "it has no description";
        if (!Kinds.Contains(source.Kind))
            yield return $"its kind '{source.Kind}' is not one of {string.Join(", ", Kinds)}";
        if (source.Tags is not { Count: > 0 } || source.Tags.Any(t => !Tags.Contains(t)) || source.Tags.Distinct().Count() != source.Tags.Count)
            yield return $"its tags must be distinct and among {string.Join(", ", Tags)}";
        if (source.Uses is not { Count: > 0 } || source.Uses.Any(u => !Uses.Contains(u)) || source.Uses.Distinct().Count() != source.Uses.Count)
            yield return $"its uses must be distinct and among {string.Join(", ", Uses)}";
        if (source.Uses?.Contains("pan") == true && source.Audio is { Channels: not 1 })
            yield return "only a mono source can be panned";
        if (source.Difficulties is not { Count: > 0 } || source.Difficulties.Any(d => d is < 1 or > 3) || source.Difficulties.Distinct().Count() != source.Difficulties.Count)
            yield return "its difficulties must be distinct and from 1 to 3";

        var license = source.License;
        if (license is null || string.IsNullOrWhiteSpace(license.Name) || string.IsNullOrWhiteSpace(license.Author)
            || !IsHttps(license.Url))
        {
            yield return "its license needs a name, an author and an https URL";
        }
        else if (source.Origin == Generated)
        {
            if (license.Name != "MIT" || license.SourceUrl is not null)
                yield return "a generated source is the repository's own: MIT, without a source URL";
        }
        else if (source.Origin == External)
        {
            if (!IsHttps(license.SourceUrl))
                yield return "an external source needs the https URL it was obtained from";
            if (license.Name.StartsWith("CC-BY", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(license.Attribution))
                yield return $"{license.Name} asks for an attribution";
        }
        else
        {
            yield return $"its origin '{source.Origin}' is neither {Generated} nor {External}";
        }

        if (source.Audio is null)
            yield return "it has no measurements: run scripts/audio-sources.cs";
    }

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^[0-9a-f]{64}\z")]
    private static partial Regex Sha256Pattern();
}

/// <summary>Reads and writes <c>sources.json</c>.</summary>
public static class AudioSourceCatalog
{
    public const string FileName = "sources.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <exception cref="InvalidDataException">The catalog is malformed, or a source breaks a rule.</exception>
    public static IReadOnlyList<AudioSource> Parse(string json)
    {
        Catalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<Catalog>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName} is malformed: {ex.Message}", ex);
        }

        var sources = catalog?.Sources ?? throw new InvalidDataException($"{FileName} has no sources.");
        var problems = sources.SelectMany(AudioSourceRules.Check)
            .Concat(sources.GroupBy(s => s.Key).Where(g => g.Count() > 1).Select(g => $"{g.Key}: listed {g.Count()} times"))
            .ToList();
        if (problems.Count > 0)
            throw new InvalidDataException($"{FileName} breaks the source rules:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        return sources;
    }

    public static string Serialize(IEnumerable<AudioSource> sources) =>
        JsonSerializer.Serialize(new Catalog([.. sources]), Options).ReplaceLineEndings("\n") + "\n";

    private sealed record Catalog(IReadOnlyList<AudioSource> Sources);
}
