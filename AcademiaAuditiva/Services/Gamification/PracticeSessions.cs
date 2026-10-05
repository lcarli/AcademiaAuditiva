namespace AcademiaAuditiva.Services.Gamification;

/// <summary>
/// One graded answer from ScoreSnapshots. <see cref="Timestamp"/> is UTC; <see cref="FilterJson"/> is the
/// preset of exercise filters it was played with (ScoreSnapshot.FilterJson), null when there were none.
/// </summary>
public readonly record struct PracticeAnswer(int ExerciseId, bool IsCorrect, DateTime Timestamp, string? FilterJson = null);

/// <summary>
/// An exercise as the badge rules see it: its name, the names of its ExerciseType, ExerciseCategory and
/// DifficultyLevel, and the option each of its filter groups starts on.
/// </summary>
/// <param name="DefaultFilters">Filter group → its first option (<see cref="ExerciseFilterPresets.Defaults"/>).</param>
public sealed record ExerciseInfo(
    int ExerciseId,
    string Name,
    string Type,
    string Category,
    string Difficulty,
    IReadOnlyDictionary<string, string> DefaultFilters);

public static class PracticeSessions
{
    /// <summary>A longer pause between two answers starts a new session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <summary>Shorter sessions are warm-ups and don't count towards session badges.</summary>
    public const int MinAnswers = 5;

    /// <summary>Splits chronologically ordered answers wherever two of them are more than <paramref name="maxGap"/> apart.</summary>
    public static List<List<PracticeAnswer>> Split(IReadOnlyList<PracticeAnswer> ordered, TimeSpan maxGap)
    {
        var runs = new List<List<PracticeAnswer>>();
        List<PracticeAnswer>? current = null;
        foreach (var answer in ordered)
        {
            if (current is null || answer.Timestamp - current[^1].Timestamp > maxGap)
            {
                current = [];
                runs.Add(current);
            }
            current.Add(answer);
        }
        return runs;
    }

    /// <summary>Sessions (pauses of at most 30 minutes) with at least <see cref="MinAnswers"/> answers.</summary>
    public static List<List<PracticeAnswer>> Counted(IReadOnlyList<PracticeAnswer> ordered) =>
        Split(ordered, SessionGap).Where(s => s.Count >= MinAnswers).ToList();

    /// <summary>True when some <paramref name="size"/> consecutive results hold at least <paramref name="minCorrect"/> correct ones.</summary>
    public static bool HasWindow(IReadOnlyList<bool> results, int size, int minCorrect)
    {
        if (size <= 0 || results.Count < size) return false;

        var correct = 0;
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i]) correct++;
            if (i >= size && results[i - size]) correct--;
            if (i >= size - 1 && correct >= minCorrect) return true;
        }
        return false;
    }

    /// <summary>True when at least <paramref name="percent"/>% of the answers are correct (integer math, so 4/5 is exactly 80%).</summary>
    public static bool AccuracyAtLeast(IReadOnlyCollection<PracticeAnswer> answers, int percent) =>
        answers.Count > 0 && answers.Count(a => a.IsCorrect) * 100 >= answers.Count * percent;
}
