using System.Globalization;
using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.Services.Dashboard;

/// <summary>One graded answer (a ScoreSnapshots row) under its exercise's name. <see cref="Timestamp"/> is UTC.</summary>
public readonly record struct DashboardAnswer(string Exercise, bool IsCorrect, DateTime Timestamp, int TimeSpentSeconds);

/// <summary>A student's totals on one exercise (a ScoreAggregates row) and the groups the exercise belongs to.</summary>
/// <param name="DifficultyOrder">Sorts the difficulty levels from the easiest.</param>
public sealed record ExerciseTotals(string Type, string Category, string Difficulty, int DifficultyOrder, int Correct, int Errors);

/// <summary>A day with answers, in the student's time zone. <see cref="Date"/> is yyyy-MM-dd.</summary>
public sealed record TimelineDay(string Date, int Answers, double Accuracy);

/// <summary>Answers to one exercise with no pause longer than <see cref="PracticeSessions.SessionGap"/>; times are UTC.</summary>
public sealed record SessionSummary(string Exercise, DateTime Start, DateTime End, int Correct, int Errors, int TimeSpentSeconds)
{
    public int Score => Correct - Errors;
}

/// <summary>A student's latest answers to one exercise and how many of them were wrong.</summary>
public sealed record ExerciseForm(string Exercise, int Answers, int Errors)
{
    public double ErrorRate => DashboardStats.Percent(Errors, Answers);

    public bool IsBelow(int accuracyPercent) => (Answers - Errors) * 100 < Answers * accuracyPercent;
}

public sealed record GroupAccuracy(string Group, double Accuracy);

public enum RecommendationKind
{
    KeepPracticing,
    Review,
    KeepCurrentLevel,
    IncreaseDifficulty
}

/// <summary>What the dashboard suggests; <see cref="Exercise"/> names the exercise to review.</summary>
public sealed record Recommendation(RecommendationKind Kind, string? Exercise = null);

/// <summary>
/// The student dashboard's figures. Per-answer figures come from ScoreSnapshots and totals from
/// ScoreAggregates: a legacy Scores row holds the running totals at one answer, so adding rows up
/// counts the same answers again and again.
/// </summary>
public static class DashboardStats
{
    /// <summary>The timeline shows this many days with answers.</summary>
    public const int TimelineDays = 30;

    public const int SessionsShown = 10;

    /// <summary>An exercise is judged on this many of its latest answers...</summary>
    public const int RecentAnswers = 20;

    /// <summary>...once it has at least this many.</summary>
    public const int MinAnswersToJudge = 5;

    public const int StrugglesShown = 5;

    /// <summary>An exercise answered right less often than this (%) lately is worth reviewing.</summary>
    public const int ReviewBelowPercent = 70;

    /// <summary>Students right at least this often (%) on every exercise lately can try harder ones.</summary>
    public const int HarderFromPercent = 90;

    public const int ReviewsShown = 3;

    public static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);

    /// <summary>The latest <paramref name="days"/> days with answers, oldest first, by the student's calendar.</summary>
    public static List<TimelineDay> Timeline(IEnumerable<DashboardAnswer> answers, TimeZoneInfo timeZone, int days = TimelineDays) =>
        answers
            .GroupBy(a => PracticeStreak.LocalDate(a.Timestamp, timeZone))
            .OrderByDescending(day => day.Key)
            .Take(days)
            .OrderBy(day => day.Key)
            .Select(day => new TimelineDay(
                day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.Count(),
                Percent(day.Count(a => a.IsCorrect), day.Count())))
            .ToList();

    /// <summary>The latest sessions, newest first.</summary>
    public static List<SessionSummary> RecentSessions(IEnumerable<DashboardAnswer> answers, int take = SessionsShown)
    {
        var sessions = new List<SessionSummary>();
        foreach (var exercise in answers.GroupBy(a => a.Exercise))
        {
            var run = new List<DashboardAnswer>();
            foreach (var answer in exercise.OrderBy(a => a.Timestamp))
            {
                if (run.Count > 0 && answer.Timestamp - run[^1].Timestamp > PracticeSessions.SessionGap)
                {
                    sessions.Add(Summarize(exercise.Key, run));
                    run = [];
                }
                run.Add(answer);
            }
            sessions.Add(Summarize(exercise.Key, run));
        }

        return sessions
            .OrderByDescending(s => s.End)
            .ThenBy(s => s.Exercise, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }

    private static SessionSummary Summarize(string exercise, List<DashboardAnswer> run)
    {
        var correct = run.Count(a => a.IsCorrect);
        return new SessionSummary(
            exercise,
            DateTime.SpecifyKind(run[0].Timestamp, DateTimeKind.Utc),
            DateTime.SpecifyKind(run[^1].Timestamp, DateTimeKind.Utc),
            correct,
            run.Count - correct,
            run.Sum(a => a.TimeSpentSeconds));
    }

    /// <summary>Each exercise's latest <paramref name="window"/> answers, for exercises with at least <paramref name="minAnswers"/>.</summary>
    public static List<ExerciseForm> RecentForm(
        IEnumerable<DashboardAnswer> answers, int window = RecentAnswers, int minAnswers = MinAnswersToJudge) =>
        answers
            .GroupBy(a => a.Exercise)
            .Select(exercise => exercise.OrderByDescending(a => a.Timestamp).Take(window).ToList())
            .Where(latest => latest.Count >= minAnswers)
            .Select(latest => new ExerciseForm(latest[0].Exercise, latest.Count, latest.Count(a => !a.IsCorrect)))
            .ToList();

    /// <summary>The exercises with the largest share of wrong answers lately; flawless ones are left out.</summary>
    public static List<ExerciseForm> Struggles(IEnumerable<ExerciseForm> form, int take = StrugglesShown) =>
        WeakestFirst(form.Where(f => f.Errors > 0)).Take(take).ToList();

    /// <summary>
    /// Review the weakest exercises (right less than <see cref="ReviewBelowPercent"/>% of the time lately);
    /// otherwise try harder exercises, or keep the current level. Without enough answers, keep practicing.
    /// </summary>
    public static List<Recommendation> Recommend(IReadOnlyCollection<ExerciseForm> form)
    {
        if (form.Count == 0)
        {
            return [new Recommendation(RecommendationKind.KeepPracticing)];
        }

        var reviews = WeakestFirst(form.Where(f => f.IsBelow(ReviewBelowPercent)))
            .Take(ReviewsShown)
            .Select(f => new Recommendation(RecommendationKind.Review, f.Exercise))
            .ToList();
        if (reviews.Count > 0)
        {
            return reviews;
        }

        var answers = form.Sum(f => f.Answers);
        var correct = answers - form.Sum(f => f.Errors);
        return [new Recommendation(correct * 100 >= answers * HarderFromPercent
            ? RecommendationKind.IncreaseDifficulty
            : RecommendationKind.KeepCurrentLevel)];
    }

    // The larger share of errors first; with the same share, the one with more answers.
    private static IOrderedEnumerable<ExerciseForm> WeakestFirst(IEnumerable<ExerciseForm> form) =>
        form.OrderByDescending(f => (double)f.Errors / f.Answers)
            .ThenByDescending(f => f.Answers)
            .ThenBy(f => f.Exercise, StringComparer.Ordinal);

    /// <summary>
    /// Share of right answers in each group of exercises that has answers, ordered by
    /// <paramref name="order"/> (the smallest in the group) and then by name.
    /// </summary>
    public static List<GroupAccuracy> AccuracyBy(
        IEnumerable<ExerciseTotals> totals, Func<ExerciseTotals, string> group, Func<ExerciseTotals, int>? order = null) =>
        totals
            .Where(t => t.Correct + t.Errors > 0)
            .GroupBy(group)
            .OrderBy(g => order is null ? 0 : g.Min(order))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new GroupAccuracy(g.Key, Percent(g.Sum(t => t.Correct), g.Sum(t => t.Correct + t.Errors))))
            .ToList();
}
