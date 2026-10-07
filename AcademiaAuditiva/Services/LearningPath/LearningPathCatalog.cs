namespace AcademiaAuditiva.Services.LearningPath;

/// <summary>
/// One exercise of the path, opened with fixed filters. The step is complete once
/// <see cref="Required"/> of the player's last <see cref="Window"/> answers on it are right.
/// </summary>
/// <param name="Exercise">Exercise.Name, which is also the ExerciseController action.</param>
/// <param name="Filters">Filter preset, in the shape of <see cref="ExerciseFilterPresets"/>.</param>
public sealed record PathStep(string Exercise, IReadOnlyDictionary<string, string> Filters, int Window, int Required);

/// <summary>A themed group of steps; <see cref="Key"/> names its texts (LearningPath.Unit.{Key}.Title).</summary>
public sealed record PathUnit(string Key, IReadOnlyList<PathStep> Steps);

/// <summary>
/// The learning path: every exercise except SolfegeMelody (it needs a microphone),
/// from pitch direction to an absolute pitch challenge. Each exercise appears once.
/// Presets are guidance, like routine items: any answer on the step's exercise counts,
/// whatever filters it was played with (older answers don't record them).
/// </summary>
public static class LearningPathCatalog
{
    public static IReadOnlyList<PathUnit> Units { get; } =
    [
        new("FirstSteps",
        [
            Step("HigherOrLower", 10, 8),
            Step("GuessInterval", 10, 7, ("keySelect", "C4"), ("scaleTypeSelect", "major")),
            Step("GuessChords", 10, 8, ("chordType", "both")),
            Step("GuessMissingNote", 10, 7, ("melodyLength", "4")),
            Step("GuessDegree", 10, 7, ("keySelect", "C"), ("scaleTypeSelect", "major"), ("gdLevel", "diatonic")),
        ]),
        new("BuildingBlocks",
        [
            Step("IntervalMelodico", 10, 7, ("keySelect", "C"), ("scaleTypeSelect", "major")),
            Step("GuessQuality", 10, 7, ("chordGroup", "all")),
            Step("CompleteChord", 10, 7, ("ccQuality", "both")),
            Step("GuessScaleType", 10, 7),
            Step("CompleteScale", 5, 4, ("csRoot", "C"), ("csScale", "major")),
            Step("GuessFunction", 10, 7, ("keySelect", "C"), ("scaleTypeSelect", "major")),
            Step("RhythmDictation", 5, 4, ("rdLevel", "1"), ("rdMeasures", "short")),
        ]),
        new("Musicianship",
        [
            Step("GuessFullInterval", 10, 7, ("intervalDirection", "asc")),
            Step("GuessInversion", 10, 7),
            Step("GuessGreekMode", 10, 7),
            Step("TransposeScale", 5, 4, ("tsScale", "major")),
            Step("GuessCadence", 10, 7),
            Step("MelodicDictation", 5, 4, ("mdLevel", "1"), ("mdMeasures", "short")),
            Step("GuessNote", 10, 5),
        ]),
    ];

    public static IReadOnlyList<PathStep> Steps { get; } = Units.SelectMany(u => u.Steps).ToList();

    private static PathStep Step(string exercise, int window, int required, params (string Group, string Value)[] filters)
        => new(exercise, filters.ToDictionary(f => f.Group, f => f.Value, StringComparer.Ordinal), window, required);
}
