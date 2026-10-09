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

    private readonly AudioSourceLibrary _sources;
    private readonly IAudioExerciseRandom _random;

    public TechnicalListeningRoundGenerator(AudioSourceLibrary sources, IAudioExerciseRandom random)
    {
        _sources = sources;
        _random = random;
    }

    public bool Supports(string exerciseName) => exerciseName == "LevelMatch";

    public TechnicalListeningRoundPlan Plan(
        Exercise exercise,
        IReadOnlyDictionary<string, string> filters)
    {
        if (!Supports(exercise.Name))
        {
            throw new ArgumentException($"No technical-listening generator exists for '{exercise.Name}'.", nameof(exercise));
        }

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

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

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
