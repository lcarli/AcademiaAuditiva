namespace AcademiaAuditiva.Services;

/// <summary>
/// Exercises that need a microphone, which not every player can use. The learning path, the daily
/// challenge and the badges that ask for every exercise or category leave them out.
/// </summary>
public static class MicrophoneExercises
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "SolfegeMelody" };

    public static bool Contains(string exerciseName) => Names.Contains(exerciseName);
}
