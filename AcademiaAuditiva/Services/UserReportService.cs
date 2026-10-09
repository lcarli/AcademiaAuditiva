using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
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

    public async Task<DashboardSummary> GetSummaryAsync(string userId, CancellationToken ct = default, string? track = null)
    {
        var aggregates = Aggregates(userId, track);
        var answers = await aggregates.SumAsync(a => a.CorrectCount + a.ErrorCount, ct);
        var bestScore = await aggregates.MaxAsync(a => (int?)a.BestScore, ct) ?? 0;
        var seconds = await Answers(userId, track).SumAsync(s => s.TimeSpentSeconds, ct);
        return new DashboardSummary(answers, bestScore, seconds);
    }

    public async Task<SkillProfile> GetSkillProfileAsync(string userId, CancellationToken ct = default, string? track = null)
    {
        var totals = await LoadTotalsAsync(userId, ct, track);
        return new SkillProfile(
            DashboardStats.AccuracyBy(totals, t => t.Type).ToDictionary(g => g.Group, g => g.Accuracy),
            DashboardStats.AccuracyBy(totals, t => t.Category).ToDictionary(g => g.Group, g => g.Accuracy));
    }

    public async Task<List<DifficultyAccuracy>> GetAccuracyByDifficultyAsync(string userId, CancellationToken ct = default, string? track = null)
    {
        var totals = string.Equals(track, TrainingTracks.Audio, StringComparison.OrdinalIgnoreCase)
            ? await LoadAudioDifficultyAsync(userId, ct)
            : await LoadTotalsAsync(userId, ct, track);
        return DashboardStats.AccuracyBy(totals, t => t.Difficulty, t => t.DifficultyOrder)
            .Select(g => new DifficultyAccuracy(g.Group, g.Accuracy))
            .ToList();
    }

    public async Task<List<TimelineDay>> GetTimelineAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default, string? track = null) =>
        DashboardStats.Timeline(await LoadAnswersAsync(userId, ct, track), timeZone);

    public async Task<List<SessionSummary>> GetRecentSessionsAsync(string userId, CancellationToken ct = default, string? track = null) =>
        DashboardStats.RecentSessions(await LoadAnswersAsync(userId, ct, track));

    public async Task<List<ExerciseForm>> GetStrugglesAsync(string userId, CancellationToken ct = default, string? track = null) =>
        DashboardStats.Struggles(DashboardStats.RecentForm(await LoadAnswersAsync(userId, ct, track)));

    public async Task<List<string>> GetRecommendationsAsync(string userId, CancellationToken ct = default, string? track = null) =>
        DashboardStats.Recommend(DashboardStats.RecentForm(await LoadAnswersAsync(userId, ct, track)))
            .Select(r => r.Kind switch
            {
                RecommendationKind.Review => _l["Report.ReviewExercise", ExerciseName(r.Exercise!)].Value,
                RecommendationKind.IncreaseDifficulty => _l["Report.IncreaseDifficulty"].Value,
                RecommendationKind.KeepCurrentLevel => _l["Report.KeepCurrentLevel"].Value,
                _ => _l["Report.KeepPracticing"].Value
            })
            .ToList();

    private IQueryable<ScoreSnapshot> Answers(string userId, string? track)
    {
        var query = _context.ScoreSnapshots.Where(s => s.UserId == userId);
        if (track is null) return query;
        var ids = TrainingTracks.Filter(_context.Exercises, track).Select(e => e.ExerciseId);
        return query.Where(s => ids.Contains(s.ExerciseId));
    }

    private IQueryable<ScoreAggregate> Aggregates(string userId, string? track)
    {
        var query = _context.ScoreAggregates.Where(a => a.UserId == userId);
        if (track is null) return query;
        var ids = TrainingTracks.Filter(_context.Exercises, track).Select(e => e.ExerciseId);
        return query.Where(a => ids.Contains(a.ExerciseId));
    }

    private Task<List<DashboardAnswer>> LoadAnswersAsync(string userId, CancellationToken ct, string? track) =>
        Answers(userId, track)
            .Select(s => new DashboardAnswer(s.Exercise!.Name, s.IsCorrect, s.Timestamp, s.TimeSpentSeconds))
            .ToListAsync(ct);

    private Task<List<ExerciseTotals>> LoadTotalsAsync(string userId, CancellationToken ct, string? track) =>
        Aggregates(userId, track)
            .Select(a => new ExerciseTotals(
                a.Exercise!.ExerciseType.Name,
                a.Exercise.ExerciseCategory.Name,
                a.Exercise.DifficultyLevel.Name,
                a.Exercise.DifficultyLevelId,
                a.CorrectCount,
                a.ErrorCount))
            .ToListAsync(ct);

    private async Task<List<ExerciseTotals>> LoadAudioDifficultyAsync(string userId, CancellationToken ct)
    {
        var answers = await Answers(userId, TrainingTracks.Audio)
            .Select(s => new
            {
                s.Exercise!.Name, s.IsCorrect, s.FilterJson,
                Difficulty = s.Exercise.DifficultyLevel.Name,
                DifficultyOrder = s.Exercise.DifficultyLevelId,
            })
            .ToListAsync(ct);
        return answers.Select(answer =>
        {
            var key = answer.Name switch
            {
                "LevelMatch" => "lmLevel",
                "StereoPosition" => "spLevel",
                "GuessFrequency" => "gfLevel",
                _ => "",
            };
            var level = ExerciseFilterPresets.Parse(answer.FilterJson).GetValueOrDefault(key);
            var (difficulty, order) = level switch
            {
                "beginner" => ("Beginner", 1),
                "intermediate" => ("Intermediate", 2),
                "advanced" => ("Advanced", 3),
                _ => (answer.Difficulty, answer.DifficultyOrder),
            };
            return new ExerciseTotals("", "", difficulty, order, answer.IsCorrect ? 1 : 0, answer.IsCorrect ? 0 : 1);
        }).ToList();
    }

    private string ExerciseName(string name)
    {
        var localized = _l[name];
        return localized.ResourceNotFound ? name : localized.Value;
    }
}
