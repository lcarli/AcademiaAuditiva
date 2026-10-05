using AcademiaAuditiva.Data;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services;

/// <summary>The totals at the top of the student dashboard.</summary>
public sealed record DashboardSummary(int Answers, int BestScore, int TotalSeconds);

/// <summary>Accuracy per exercise type (the radar) and per category.</summary>
public sealed record SkillProfile(IReadOnlyDictionary<string, double> Radar, IReadOnlyDictionary<string, double> ByCategory);

public sealed record DifficultyAccuracy(string Difficulty, double Accuracy);

/// <summary>
/// Loads a student's practice for the dashboard and has <see cref="DashboardStats"/> work out its figures:
/// answers from ScoreSnapshots and totals from ScoreAggregates, never the running totals in Scores.
/// </summary>
public class UserReportService
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResources> _l;

    public UserReportService(ApplicationDbContext context, IStringLocalizer<SharedResources> localizer)
    {
        _context = context;
        _l = localizer;
    }

    public async Task<DashboardSummary> GetSummaryAsync(string userId, CancellationToken ct = default)
    {
        var aggregates = _context.ScoreAggregates.Where(a => a.UserId == userId);
        var answers = await aggregates.SumAsync(a => a.CorrectCount + a.ErrorCount, ct);
        var bestScore = await aggregates.MaxAsync(a => (int?)a.BestScore, ct) ?? 0;
        var seconds = await _context.ScoreSnapshots.Where(s => s.UserId == userId).SumAsync(s => s.TimeSpentSeconds, ct);
        return new DashboardSummary(answers, bestScore, seconds);
    }

    public async Task<SkillProfile> GetSkillProfileAsync(string userId, CancellationToken ct = default)
    {
        var totals = await LoadTotalsAsync(userId, ct);
        return new SkillProfile(
            DashboardStats.AccuracyBy(totals, t => t.Type).ToDictionary(g => g.Group, g => g.Accuracy),
            DashboardStats.AccuracyBy(totals, t => t.Category).ToDictionary(g => g.Group, g => g.Accuracy));
    }

    public async Task<List<DifficultyAccuracy>> GetAccuracyByDifficultyAsync(string userId, CancellationToken ct = default) =>
        DashboardStats.AccuracyBy(await LoadTotalsAsync(userId, ct), t => t.Difficulty, t => t.DifficultyOrder)
            .Select(g => new DifficultyAccuracy(g.Group, g.Accuracy))
            .ToList();

    public async Task<List<TimelineDay>> GetTimelineAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default) =>
        DashboardStats.Timeline(await LoadAnswersAsync(userId, ct), timeZone);

    public async Task<List<SessionSummary>> GetRecentSessionsAsync(string userId, CancellationToken ct = default) =>
        DashboardStats.RecentSessions(await LoadAnswersAsync(userId, ct));

    public async Task<List<ExerciseForm>> GetStrugglesAsync(string userId, CancellationToken ct = default) =>
        DashboardStats.Struggles(DashboardStats.RecentForm(await LoadAnswersAsync(userId, ct)));

    public async Task<List<string>> GetRecommendationsAsync(string userId, CancellationToken ct = default) =>
        DashboardStats.Recommend(DashboardStats.RecentForm(await LoadAnswersAsync(userId, ct)))
            .Select(r => r.Kind switch
            {
                RecommendationKind.Review => _l["Report.ReviewExercise", ExerciseName(r.Exercise!)].Value,
                RecommendationKind.IncreaseDifficulty => _l["Report.IncreaseDifficulty"].Value,
                RecommendationKind.KeepCurrentLevel => _l["Report.KeepCurrentLevel"].Value,
                _ => _l["Report.KeepPracticing"].Value
            })
            .ToList();

    private Task<List<DashboardAnswer>> LoadAnswersAsync(string userId, CancellationToken ct) =>
        _context.ScoreSnapshots
            .Where(s => s.UserId == userId)
            .Select(s => new DashboardAnswer(s.Exercise!.Name, s.IsCorrect, s.Timestamp, s.TimeSpentSeconds))
            .ToListAsync(ct);

    private Task<List<ExerciseTotals>> LoadTotalsAsync(string userId, CancellationToken ct) =>
        _context.ScoreAggregates
            .Where(a => a.UserId == userId)
            .Select(a => new ExerciseTotals(
                a.Exercise!.ExerciseType.Name,
                a.Exercise.ExerciseCategory.Name,
                a.Exercise.DifficultyLevel.Name,
                a.Exercise.DifficultyLevelId,
                a.CorrectCount,
                a.ErrorCount))
            .ToListAsync(ct);

    private string ExerciseName(string name)
    {
        var localized = _l[name];
        return localized.ResourceNotFound ? name : localized.Value;
    }
}
