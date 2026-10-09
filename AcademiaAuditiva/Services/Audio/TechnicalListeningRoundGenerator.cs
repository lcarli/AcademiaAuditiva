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

    private readonly AudioSourceLibrary _sources;
    private readonly IAudioExerciseRandom _random;

    public TechnicalListeningRoundGenerator(AudioSourceLibrary sources, IAudioExerciseRandom random)
    {
        _sources = sources;
        _random = random;
    }

    public bool Supports(string exerciseName) => exerciseName is "LevelMatch" or "StereoPosition";

    public TechnicalListeningRoundPlan Plan(
        Exercise exercise,
        IReadOnlyDictionary<string, string> filters)
    {
        if (!Supports(exercise.Name))
        {
            throw new ArgumentException($"No technical-listening generator exists for '{exercise.Name}'.", nameof(exercise));
        }

        return exercise.Name == "StereoPosition"
            ? PlanStereoPosition(filters)
            : PlanLevelMatch(filters);
    }

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
}

public sealed record NamedAudioProcessingPlan(string Key, AudioProcessingPlan Plan);

public sealed record TechnicalListeningRoundPlan(
    string ExpectedAnswerJson,
    IReadOnlyList<NamedAudioProcessingPlan> Clips,
    IReadOnlyDictionary<string, string> AnswerMetadata);
