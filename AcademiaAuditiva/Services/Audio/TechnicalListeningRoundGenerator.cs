using System.Globalization;
using System.Security.Cryptography;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Newtonsoft.Json;

namespace AcademiaAuditiva.Services.Audio;

public interface IAudioExerciseRandom
{
    int Next(int exclusiveMaximum);
}

public sealed class CryptoAudioExerciseRandom : IAudioExerciseRandom
{
    public int Next(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum);
}

/// <summary>
/// Builds answer-bearing processing plans for the Audio track. Processing
/// parameters remain in the server-side expected answer and never become
/// response metadata.
/// </summary>
public sealed class TechnicalListeningRoundGenerator
{
    private static readonly IReadOnlyDictionary<string, LevelMatchProfile> LevelMatchProfiles =
        new Dictionary<string, LevelMatchProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["beginner"] = new(1, [6, 9, 12], RequiresDifference: false),
            ["intermediate"] = new(2, [2, 3, 4, 6], RequiresDifference: true),
            ["advanced"] = new(3, [0.5, 1, 1.5, 2], RequiresDifference: true),
        };

    // Pan convention: -1 is hard left, 0 the centre, +1 hard right. A code names
    // the position on the answer buttons: L/R are the hard edges of the
    // beginner profile, the numbers are the percentage towards that side.
    private static readonly IReadOnlyDictionary<string, StereoPositionProfile> StereoPositionProfiles =
        new Dictionary<string, StereoPositionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["beginner"] = new(1, [new("L", -1), new("C", 0), new("R", 1)]),
            ["intermediate"] = new(2,
            [
                new("L75", -0.75), new("L25", -0.25), new("C", 0), new("R25", 0.25), new("R75", 0.75),
            ]),
            ["advanced"] = new(3,
            [
                new("L75", -0.75), new("L50", -0.5), new("L25", -0.25), new("C", 0),
                new("R25", 0.25), new("R50", 0.5), new("R75", 0.75),
            ]),
        };

    private static readonly IReadOnlyDictionary<string, GuessFrequencyProfile> GuessFrequencyProfiles =
        new Dictionary<string, GuessFrequencyProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["beginner"] = new(1, [100, 500, 1000, 5000, 10000], 9, 1),
            ["intermediate"] = new(2, [125, 250, 500, 1000, 2000, 4000, 8000], 6, 1),
            ["advanced"] = new(3, [500, 630, 800, 1000, 1250, 1600, 2000], 6, 2),
        };

    // A source must support every offered band of the profile, not just the
    // answer: identifying its timbre must not rule out buttons. Tests measure every pair.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> FrequencySourceBands =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
        {
            ["pink-noise"] = [100, 125, 250, 500, 1000, 2000, 4000, 5000, 8000, 10000],
            ["synth-chords"] = [500, 630, 800, 1000, 1250, 1600, 2000],
            ["full-mix"] = [125, 250, 500, 630, 800, 1000, 1250, 1600, 2000, 8000],
            ["eq-reference-mix"] = [100, 125, 250, 500, 630, 800, 1000, 1250, 1600, 2000, 4000, 5000, 8000, 10000],
        };

    private readonly AudioSourceLibrary _sources;
    private readonly IAudioExerciseRandom _random;

    public TechnicalListeningRoundGenerator(AudioSourceLibrary sources, IAudioExerciseRandom random)
    {
        _sources = sources;
        _random = random;
    }

    public bool Supports(string exerciseName) => exerciseName is "LevelMatch" or "StereoPosition" or "GuessFrequency";

    public TechnicalListeningRoundPlan Plan(
        Exercise exercise,
        IReadOnlyDictionary<string, string> filters)
    {
        return exercise.Name switch
        {
            "LevelMatch" => PlanLevelMatch(filters),
            "StereoPosition" => PlanStereoPosition(filters),
            "GuessFrequency" => PlanGuessFrequency(filters),
            _ => throw new ArgumentException($"No technical-listening generator exists for '{exercise.Name}'.", nameof(exercise)),
        };
    }

    private TechnicalListeningRoundPlan PlanGuessFrequency(IReadOnlyDictionary<string, string> filters)
    {
        var requestedLevel = filters.GetValueOrDefault("gfLevel");
        var level = requestedLevel is not null && GuessFrequencyProfiles.ContainsKey(requestedLevel)
            ? requestedLevel.ToLowerInvariant()
            : "beginner";
        var profile = GuessFrequencyProfiles[level];
        var frequency = profile.Frequencies[_random.Next(profile.Frequencies.Count)];
        var eligible = FrequencySourcesFor(level, frequency);
        if (eligible.Count < 2)
        {
            throw new InvalidOperationException($"Frequency {frequency} Hz at {level} needs at least two eligible EQ sources.");
        }

        var source = eligible[_random.Next(eligible.Count)];
        var boostedClip = _random.Next(2) == 0 ? "A" : "B";
        // Both clips get the same randomized target, with extra headroom for EQ.
        var targetLufs = -26 - _random.Next(13) / 2.0;
        var clips = new[] { "A", "B" }.Select(key => new NamedAudioProcessingPlan(
            key,
            new AudioProcessingPlan(source.Key, key == boostedClip
                ? [new PeakingEqProcessor(frequency, profile.GainDb, profile.Q), new LoudnessMatchProcessor(targetLufs)]
                : [new LoudnessMatchProcessor(targetLufs)]))).ToArray();
        foreach (var clip in clips)
        {
            AudioProcessing.Validate(clip.Plan, source);
        }

        var region = frequency switch
        {
            <= 250 => "bass",
            <= 500 => "lowMids",
            <= 2000 => "mids",
            <= 5000 => "presence",
            _ => "air",
        };
        var expected = JsonConvert.SerializeObject(new
        {
            answer = frequency.ToString(CultureInfo.InvariantCulture),
            frequencyHz = frequency,
            boostedClip,
            region,
            level,
            sourceKind = source.Kind,
        });
        var answerMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gfFrequencyHz"] = frequency.ToString(CultureInfo.InvariantCulture),
            ["gfSourceKind"] = source.Kind,
        };
        return new TechnicalListeningRoundPlan(expected, clips, answerMetadata);
    }

    public static IReadOnlyList<int> FrequenciesFor(string level) =>
        GuessFrequencyProfiles[level].Frequencies;

    internal IReadOnlyList<AudioSource> FrequencySourcesFor(string level, int frequency) =>
        _sources.Sources.Where(source =>
            GuessFrequencyProfiles[level].Frequencies.Contains(frequency)
            && source.Uses.Contains("eq", StringComparer.Ordinal)
            && source.Difficulties.Contains(GuessFrequencyProfiles[level].Difficulty)
            && FrequencySourceBands.TryGetValue(source.Key, out var bands)
            && GuessFrequencyProfiles[level].Frequencies.All(bands.Contains)
            && frequency <= AudioProcessing.MaxEqFrequencyRateRatio * source.Audio.SampleRate).ToArray();

    private TechnicalListeningRoundPlan PlanStereoPosition(IReadOnlyDictionary<string, string> filters)
    {
        var requestedLevel = filters.GetValueOrDefault("spLevel");
        var level = requestedLevel is not null && StereoPositionProfiles.ContainsKey(requestedLevel)
            ? requestedLevel.ToLowerInvariant()
            : "beginner";
        var profile = StereoPositionProfiles[level];
        var eligible = _sources.Sources
            .Where(s => s.Uses.Contains("pan", StringComparer.Ordinal)
                && s.Difficulties.Contains(profile.Difficulty))
            .ToArray();
        if (eligible.Length == 0)
        {
            throw new InvalidOperationException($"No pan source supports difficulty {profile.Difficulty}.");
        }

        var source = eligible[_random.Next(eligible.Length)];
        var position = profile.Positions[_random.Next(profile.Positions.Count)];

        // The constant-power pan law keeps the loudness the same at every
        // position and never raises the peak, so no gain compensation is needed.
        var clip = new NamedAudioProcessingPlan(
            "A",
            new AudioProcessingPlan(source.Key, [new PanProcessor(position.Pan)]));
        AudioProcessing.Validate(clip.Plan, source);

        var expected = JsonConvert.SerializeObject(new
        {
            answer = position.Code,
            position = position.Pan,
            level,
            sourceKind = source.Kind,
        });
        var answerMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["spPosition"] = Number(position.Pan),
            ["spSourceKind"] = source.Kind,
        };
        return new TechnicalListeningRoundPlan(expected, [clip], answerMetadata);
    }

    private TechnicalListeningRoundPlan PlanLevelMatch(IReadOnlyDictionary<string, string> filters)
    {
        var requestedLevel = filters.GetValueOrDefault("lmLevel");
        var level = requestedLevel is not null && LevelMatchProfiles.ContainsKey(requestedLevel)
            ? requestedLevel.ToLowerInvariant()
            : "beginner";
        var profile = LevelMatchProfiles[level];
        var eligible = _sources.Sources
            .Where(s => s.Uses.Contains("level", StringComparer.Ordinal)
                && s.Difficulties.Contains(profile.Difficulty))
            .ToArray();
        if (eligible.Length == 0)
        {
            throw new InvalidOperationException($"No level source supports difficulty {profile.Difficulty}.");
        }

        var source = eligible[_random.Next(eligible.Length)];
        var differenceDb = profile.DifferencesDb[_random.Next(profile.DifferencesDb.Count)];
        var louder = _random.Next(2) == 0 ? "A" : "B";

        // The common offset changes the absolute playback level without changing
        // A versus B. Half-dB steps over a six-dB window stay reproducible in
        // tests and always leave the louder plan below the measured peak limit.
        var safeUpperDb = Math.Min(0, AudioProcessing.MaxPeakDbfs - source.Audio.PeakDbfs - differenceDb);
        var upperHalfDb = (int)Math.Floor(safeUpperDb * 2);
        var lowerHalfDb = Math.Max((int)(AudioProcessing.MinGainDb * 2), upperHalfDb - 12);
        var commonGainDb = (lowerHalfDb + _random.Next(upperHalfDb - lowerHalfDb + 1)) / 2.0;

        var aGainDb = commonGainDb + (louder == "A" ? differenceDb : 0);
        var bGainDb = commonGainDb + (louder == "B" ? differenceDb : 0);
        var clips = new[]
        {
            new NamedAudioProcessingPlan("A", new AudioProcessingPlan(source.Key, [new GainProcessor(aGainDb)])),
            new NamedAudioProcessingPlan("B", new AudioProcessingPlan(source.Key, [new GainProcessor(bGainDb)])),
        };
        foreach (var clip in clips)
        {
            AudioProcessing.Validate(clip.Plan, source);
        }

        var difference = Number(differenceDb);
        var answer = profile.RequiresDifference ? $"{louder}|{difference}" : louder;
        var expected = JsonConvert.SerializeObject(new
        {
            answer,
            louder,
            differenceDb,
            requiresDifference = profile.RequiresDifference,
            level,
            sourceKind = source.Kind,
        });
        var answerMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lmDifferenceDb"] = difference,
            ["lmSourceKind"] = source.Kind,
        };
        return new TechnicalListeningRoundPlan(expected, clips, answerMetadata);
    }

    internal static IReadOnlyList<double> DifferencesFor(string level) =>
        LevelMatchProfiles[level].DifferencesDb;

    internal static IReadOnlyList<StereoPosition> PositionsFor(string level) =>
        StereoPositionProfiles[level].Positions;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal sealed record StereoPosition(string Code, double Pan);

    private sealed record StereoPositionProfile(int Difficulty, IReadOnlyList<StereoPosition> Positions);

    private sealed record LevelMatchProfile(
        int Difficulty,
        IReadOnlyList<double> DifferencesDb,
        bool RequiresDifference);

    private sealed record GuessFrequencyProfile(
        int Difficulty,
        IReadOnlyList<int> Frequencies,
        double GainDb,
        double Q);
}

public sealed record NamedAudioProcessingPlan(string Key, AudioProcessingPlan Plan);

public sealed record TechnicalListeningRoundPlan(
    string ExpectedAnswerJson,
    IReadOnlyList<NamedAudioProcessingPlan> Clips,
    IReadOnlyDictionary<string, string> AnswerMetadata);
