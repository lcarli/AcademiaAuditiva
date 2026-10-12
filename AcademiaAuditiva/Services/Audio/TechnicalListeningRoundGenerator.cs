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
public sealed partial class TechnicalListeningRoundGenerator
{
    private const int GuessFrequencyAnswerCount = 4;

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

    public bool Supports(string exerciseName) => exerciseName is
        "LevelMatch" or "StereoPosition" or "GuessFrequency" || AudioComparisonCatalog.Find(exerciseName) is not null;

    public TechnicalListeningRoundPlan Plan(
        Exercise exercise,
        IReadOnlyDictionary<string, string> filters)
    {
        return exercise.Name switch
        {
            "LevelMatch" => PlanLevelMatch(filters),
            "StereoPosition" => PlanStereoPosition(filters),
            "GuessFrequency" => PlanGuessFrequency(filters),
            "BoostOrCut" => PlanBoostOrCut(filters),
            "GuessQ" => PlanGuessQ(filters),
            "Compression" => PlanCompression(filters),
            "EqMatching" => PlanEqMatching(filters),
            _ when AudioComparisonCatalog.Find(exercise.Name) is { } definition => PlanExpansion(definition, filters),
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
        var options = FrequencyOptions(profile.Frequencies, frequency)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new TechnicalListeningRoundPlan(expected, clips, answerMetadata, options);
    }

    public static IReadOnlyList<int> FrequenciesFor(string level) =>
        GuessFrequencyProfiles[level].Frequencies;

    internal IReadOnlyList<AudioSource> FrequencySourcesFor(string level, int frequency) =>
        PreferMusic(_sources.Sources.Where(source =>
            GuessFrequencyProfiles[level].Frequencies.Contains(frequency)
            && source.Uses.Contains("eq", StringComparer.Ordinal)
            && source.Difficulties.Contains(GuessFrequencyProfiles[level].Difficulty)
            && (source.IsRemote
                && AudioSourceLibrary.SupportsStorageEqDifficulty(source.Key, GuessFrequencyProfiles[level].Difficulty)
                || FrequencySourceBands.TryGetValue(source.Key, out var bands)
                    && GuessFrequencyProfiles[level].Frequencies.All(bands.Contains))
            && frequency <= AudioProcessing.MaxEqFrequencyRateRatio * source.Audio.SampleRate));

    private static IReadOnlyList<AudioSource> PreferMusic(IEnumerable<AudioSource> sources)
    {
        var eligible = sources.ToArray();
        var music = eligible.Where(source => source.IsRemote).ToArray();
        return music.Length > 0 ? music : eligible;
    }

    public static string LevelFilterFor(string exerciseName) => exerciseName switch
    {
        "LevelMatch" => "lmLevel",
        "StereoPosition" => "spLevel",
        "GuessFrequency" => "gfLevel",
        "BoostOrCut" => "bcLevel",
        "GuessQ" => "gqLevel",
        "Compression" => "cpLevel",
        _ => AudioComparisonCatalog.Find(exerciseName)?.LevelFilter
            ?? throw new ArgumentException("Unknown Audio exercise.", nameof(exerciseName)),
    };

    private static string Level(IReadOnlyDictionary<string, string> filters, string key) =>
        filters.GetValueOrDefault(key)?.ToLowerInvariant() is "intermediate" ? "intermediate"
            : filters.GetValueOrDefault(key)?.ToLowerInvariant() is "advanced" ? "advanced" : "beginner";

    private TechnicalListeningRoundPlan PlanBoostOrCut(IReadOnlyDictionary<string, string> filters)
    {
        var level = Level(filters, "bcLevel");
        var gain = level == "beginner" ? 9 : level == "intermediate" ? 6 : 3;
        var regions = new[]
        {
            ("sub", 100), ("bass", 125), ("lowMids", 250), ("mids", 1000),
            ("upperMids", 2000), ("presence", 4000), ("air", 8000),
        };
        var (region, frequency) = regions[_random.Next(regions.Length)];
        var direction = _random.Next(2) == 0 ? "boost" : "cut";
        var source = Pick(EqSourcesForFrequency(frequency), "BoostOrCut");
        var eq = new PeakingEqProcessor(frequency, direction == "boost" ? gain : -gain, 1);
        return Comparison(source, eq, "B", new
        {
            answer = $"{direction}|{region}", direction, region, frequencyHz = frequency,
            gainDb = eq.GainDb, level, sourceKind = source.Kind,
        }, new Dictionary<string, string>
        {
            ["bcDirection"] = direction, ["bcRegion"] = region,
            ["bcFrequencyHz"] = Number(frequency), ["bcSourceKind"] = source.Kind,
        });
    }

    private IReadOnlyList<AudioSource> EqSourcesForFrequency(int frequency) =>
        PreferMusic(_sources.Sources.Where(source =>
            source.Uses.Contains("eq", StringComparer.Ordinal)
            && source.Difficulties.Contains(2)
            && (source.IsRemote
                ? AudioSourceLibrary.SupportsStorageEqDifficulty(source.Key, 2)
                : FrequencySourceBands.TryGetValue(source.Key, out var bands) && bands.Contains(frequency))
            && frequency <= AudioProcessing.MaxEqFrequencyRateRatio * source.Audio.SampleRate));

    private TechnicalListeningRoundPlan PlanGuessQ(IReadOnlyDictionary<string, string> filters)
    {
        var level = Level(filters, "gqLevel");
        var frequency = new[] { 500, 1000, 2000 }[_random.Next(3)];
        var widths = new[] { "broad", "medium", "narrow" };
        var qValues = level == "beginner" ? new[] { 0.35, 1.0, 4.0 }
            : level == "intermediate" ? [0.5, 1.5, 4.0] : [0.5, 1.0, 2.0, 4.0, 8.0];
        var index = _random.Next(qValues.Length);
        var q = qValues[index];
        var answer = level == "advanced" ? Number(q) : widths[index];
        var source = Pick(FrequencySourcesFor("intermediate", frequency), "GuessQ");
        return Comparison(source, new PeakingEqProcessor(frequency, 9, q), "B", new
        {
            answer, q, frequencyHz = frequency, level, sourceKind = source.Kind,
        }, new Dictionary<string, string>
        {
            ["gqWidth"] = answer, ["gqQ"] = Number(q),
            ["gqFrequencyHz"] = Number(frequency), ["gqSourceKind"] = source.Kind,
        });
    }

    private TechnicalListeningRoundPlan PlanCompression(IReadOnlyDictionary<string, string> filters)
    {
        var level = Level(filters, "cpLevel");
        var source = Pick(PreferMusic(_sources.Sources.Where(source =>
            source.IsRemote ? AudioSourceLibrary.SupportsStorageCompression(source.Key)
                : source.Key is "drum-loop" or "full-mix")), "Compression");
        var compressedClip = _random.Next(2) == 0 ? "A" : "B";
        var compressor = CompressionFor(level);
        return Comparison(source, compressor, compressedClip, new
        {
            answer = compressedClip, compressedClip, thresholdDbfs = compressor.ThresholdDbfs, ratio = compressor.Ratio,
            attackMs = compressor.AttackMs, releaseMs = compressor.ReleaseMs, level, sourceKind = source.Kind,
        }, new Dictionary<string, string>
        {
            ["cpCompressedClip"] = compressedClip, ["cpRatio"] = Number(compressor.Ratio),
            ["cpSourceKind"] = source.Kind,
        });
    }

    private TechnicalListeningRoundPlan PlanEqMatching(IReadOnlyDictionary<string, string> filters)
    {
        var level = Level(filters, "eqmLevel");
        var (minFrequency, maxFrequency, maxGain, minQ, maxQ) = level switch
        {
            "beginner" => (250.0, 4000.0, 6.0, 0.7, 3.0),
            "intermediate" => (120.0, 8000.0, 9.0, 0.5, 6.0),
            _ => (60.0, 12000.0, 12.0, 0.5, 8.0),
        };
        var frequency = Math.Pow(2, Math.Log2(minFrequency)
            + UnitRandom() * (Math.Log2(maxFrequency) - Math.Log2(minFrequency)));
        var gainMagnitude = 3 + UnitRandom() * (maxGain - 3);
        var gain = _random.Next(2) == 0 ? gainMagnitude : -gainMagnitude;
        var q = Math.Pow(2, Math.Log2(minQ) + UnitRandom() * (Math.Log2(maxQ) - Math.Log2(minQ)));
        var source = Pick(FrequencySourcesFor("intermediate", 1000), "EqMatching");
        var target = new PeakingEqProcessor(frequency, gain, q);
        var targetLufs = -30 - _random.Next(9) / 2.0;
        var clips = new[]
        {
            new NamedAudioProcessingPlan("A", new(source.Key, [new LoudnessMatchProcessor(targetLufs)])),
            new NamedAudioProcessingPlan("B", new(source.Key, [target, new LoudnessMatchProcessor(targetLufs)])),
        };
        foreach (var clip in clips) AudioProcessing.Validate(clip.Plan, source);
        return new TechnicalListeningRoundPlan(
            JsonConvert.SerializeObject(new
            {
                answer = FormattableString.Invariant($"{frequency:R}|{gain:R}|{q:R}"),
                frequencyHz = frequency, gainDb = gain, q, level,
            }),
            clips,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private double UnitRandom() => _random.Next(1_000_000) / 1_000_000.0;

    internal static CompressorProcessor CompressionFor(string level) => level switch
    {
        "beginner" => new(-28, 6, 0, 20),
        "intermediate" => new(-24, 3, 0, 20),
        "advanced" => new(-22, 2, 0, 20),
        _ => throw new ArgumentException("Unknown compression level.", nameof(level)),
    };

    private AudioSource Pick(IReadOnlyList<AudioSource> sources, string exercise) =>
        sources.Count > 0 ? sources[_random.Next(sources.Count)]
            : throw new InvalidOperationException($"No qualified audio sources for {exercise}.");

    private TechnicalListeningRoundPlan Comparison(
        AudioSource source, AudioProcessor processor, string processedClip,
        object expected, IReadOnlyDictionary<string, string> metadata)
    {
        var target = new LoudnessMatchProcessor(-28 - _random.Next(9) / 2.0);
        var clips = new[] { "A", "B" }.Select(key => new NamedAudioProcessingPlan(key,
            new AudioProcessingPlan(source.Key, key == processedClip ? [processor, target] : [target]))).ToArray();
        foreach (var clip in clips) AudioProcessing.Validate(clip.Plan, source);
        return new TechnicalListeningRoundPlan(JsonConvert.SerializeObject(expected), clips, metadata);
    }

    private IReadOnlyList<int> FrequencyOptions(IReadOnlyList<int> frequencies, int answer)
    {
        var alternatives = frequencies.Where(frequency => frequency != answer).ToArray();
        var start = _random.Next(alternatives.Length);
        var answerAt = _random.Next(GuessFrequencyAnswerCount);
        var options = new int[GuessFrequencyAnswerCount];
        for (var i = 0; i < options.Length; i++)
        {
            options[i] = i == answerAt
                ? answer
                : alternatives[(start + (i < answerAt ? i : i - 1)) % alternatives.Length];
        }
        return options;
    }

    private TechnicalListeningRoundPlan PlanStereoPosition(IReadOnlyDictionary<string, string> filters)
    {
        var requestedLevel = filters.GetValueOrDefault("spLevel");
        var level = requestedLevel is not null && StereoPositionProfiles.ContainsKey(requestedLevel)
            ? requestedLevel.ToLowerInvariant()
            : "beginner";
        var profile = StereoPositionProfiles[level];
        var eligible = PreferMusic(_sources.Sources
            .Where(s => s.Uses.Contains("pan", StringComparer.Ordinal)
                && s.Difficulties.Contains(profile.Difficulty)));
        if (eligible.Count == 0)
        {
            throw new InvalidOperationException($"No pan source supports difficulty {profile.Difficulty}.");
        }

        var source = eligible[_random.Next(eligible.Count)];
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
        var eligible = PreferMusic(_sources.Sources
            .Where(s => s.Uses.Contains("level", StringComparer.Ordinal)
                && s.Difficulties.Contains(profile.Difficulty)));
        if (eligible.Count == 0)
        {
            throw new InvalidOperationException($"No level source supports difficulty {profile.Difficulty}.");
        }

        var source = eligible[_random.Next(eligible.Count)];
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
    IReadOnlyDictionary<string, string> AnswerMetadata,
    IReadOnlyList<string>? AnswerOptions = null);
