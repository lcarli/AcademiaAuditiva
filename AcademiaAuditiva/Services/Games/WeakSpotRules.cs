using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.Services.Games;

/// <summary>An exercise, played with given settings, the player often gets wrong.</summary>
/// <param name="FilterJson">The settings, as saved with the answers (<c>ScoreSnapshot.FilterJson</c>); null for none.</param>
/// <param name="Correct">Right answers among the last <see cref="Answered"/>.</param>
public sealed record WeakSpot(int ExerciseId, string? FilterJson, int Correct, int Answered)
{
    public int Percent => Answered == 0 ? 0 : Correct * 100 / Answered;
}

public static class WeakSpotRules
{
    /// <summary>Answers an exercise and settings need before they can be called a weak spot.</summary>
    public const int MinAnswers = 10;

    /// <summary>How many of the latest answers are looked at, so old mistakes fade.</summary>
    public const int Window = 20;

    /// <summary>Weak below this share of right answers, in percent.</summary>
    public const int Threshold = 80;

    public const int MaxSpots = 5;

    /// <summary>
    /// The exercises and settings with at least <see cref="MinAnswers"/> answers, fewer than
    /// <see cref="Threshold"/>% of the last <see cref="Window"/> of them right, weakest first.
    /// </summary>
    /// <param name="answers">The player's answers, oldest first.</param>
    /// <param name="playable">Whether an exercise can be played as a game (the sung ones can't).</param>
    public static IReadOnlyList<WeakSpot> Find(IReadOnlyList<PracticeAnswer> answers, Func<int, bool> playable)
    {
        return answers
            .Where(a => playable(a.ExerciseId))
            .GroupBy(a => (a.ExerciseId, a.FilterJson))
            .Where(g => g.Count() >= MinAnswers)
            .Select(g =>
            {
                var recent = g.TakeLast(Window).ToList();
                return new WeakSpot(g.Key.ExerciseId, g.Key.FilterJson, recent.Count(a => a.IsCorrect), recent.Count);
            })
            .Where(s => s.Correct * 100 < Threshold * s.Answered)
            .OrderBy(s => (double)s.Correct / s.Answered)
            .ThenByDescending(s => s.Answered)
            .ThenBy(s => s.ExerciseId)
            .ThenBy(s => s.FilterJson, StringComparer.Ordinal)
            .Take(MaxSpots)
            .ToList();
    }
}
