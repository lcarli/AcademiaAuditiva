using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.LearningPath;

/// <summary>One step as the pages show it: its place on the path, its preset and the player's progress.</summary>
/// <param name="Number">1-based position on the whole path.</param>
/// <param name="Filters">The catalog preset, sanitized against the exercise's filter groups.</param>
public sealed record StepProgress(
    int Number,
    int ExerciseId,
    StepStatus Status,
    IReadOnlyDictionary<string, string> Filters,
    IReadOnlyList<AppliedFilter> AppliedFilters)
{
    public string Exercise => Status.Step.Exercise;
    public StepState State => Status.State;
    public int Window => Status.Step.Window;
    public int Required => Status.Step.Required;
    public int Correct => Status.Correct;
    public int Answered => Status.Answered;
    public int Percent => Status.Percent;
}

public sealed record UnitProgress(string Key, int Number, IReadOnlyList<StepProgress> Steps)
{
    public int CompletedSteps => Steps.Count(s => s.State == StepState.Completed);

    public StepState State =>
        CompletedSteps == Steps.Count ? StepState.Completed
        : Steps.Any(s => s.State == StepState.Current) ? StepState.Current
        : StepState.Locked;
}

/// <param name="JustCompleted">The step the player's latest answer completed, if it completed one.</param>
public sealed record LearningPathProgress(IReadOnlyList<UnitProgress> Units, StepProgress? JustCompleted)
{
    public IEnumerable<StepProgress> Steps => Units.SelectMany(u => u.Steps);

    public int TotalSteps => Units.Sum(u => u.Steps.Count);

    public int CompletedSteps => Units.Sum(u => u.CompletedSteps);

    /// <summary>The step the player is working on; null once the whole path is complete.</summary>
    public StepProgress? Current => Steps.FirstOrDefault(s => s.State == StepState.Current);

    public bool IsComplete => TotalSteps > 0 && CompletedSteps == TotalSteps;

    public bool HasStarted => CompletedSteps > 0 || Current?.Answered > 0;

    public int Percent => TotalSteps == 0 ? 0 : CompletedSteps * 100 / TotalSteps;

    public UnitProgress UnitOf(StepProgress step) => Units.First(u => u.Steps.Any(s => s.Number == step.Number));
}

public interface ILearningPathService
{
    /// <summary>The player's progress on every step of the learning path.</summary>
    Task<LearningPathProgress> GetProgressAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Evaluates <see cref="LearningPathCatalog"/> against the player's answers.
/// Nothing is stored: progress is recomputed from the answers on each call, which come from
/// <see cref="PracticeHistory"/>, so an answer reads them once for XP, badges and the path.
/// </summary>
public sealed class LearningPathService : ILearningPathService
{
    private readonly ApplicationDbContext _db;
    private readonly PracticeHistory _history;

    public LearningPathService(ApplicationDbContext db, PracticeHistory history)
    {
        _db = db;
        _history = history;
    }

    public async Task<LearningPathProgress> GetProgressAsync(string userId, CancellationToken ct = default)
    {
        var names = LearningPathCatalog.Steps.Select(s => s.Exercise).ToList();
        var exercises = (await _db.Exercises.AsNoTracking()
                .Where(e => names.Contains(e.Name))
                .Select(e => new { e.ExerciseId, e.Name, e.FiltersJson })
                .ToListAsync(ct))
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ExerciseId).First(), StringComparer.Ordinal);

        // A step whose exercise is not seeded is left out instead of blocking the path.
        var units = LearningPathCatalog.Units
            .Select(u => new PathUnit(u.Key, u.Steps.Where(s => exercises.ContainsKey(s.Exercise)).ToList()))
            .Where(u => u.Steps.Count > 0)
            .ToList();
        var exerciseIds = exercises.ToDictionary(kv => kv.Key, kv => kv.Value.ExerciseId, StringComparer.Ordinal);
        var ids = exerciseIds.Values.ToHashSet();

        var answers = (await _history.GetAsync(userId, ct))
            .Where(a => ids.Contains(a.ExerciseId))
            .ToList();

        var evaluation = LearningPathEvaluator.Evaluate(units.SelectMany(u => u.Steps).ToList(), exerciseIds, answers);

        var index = 0;
        StepProgress? justCompleted = null;
        var unitRows = new List<UnitProgress>(units.Count);
        foreach (var unit in units)
        {
            var rows = new List<StepProgress>(unit.Steps.Count);
            foreach (var step in unit.Steps)
            {
                var exercise = exercises[step.Exercise];
                var groups = ExerciseFilterPresets.Groups(exercise.FiltersJson);
                // Only known groups and options reach the exercise URL.
                var filters = ExerciseFilterPresets.Sanitize(
                    step.Filters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)), groups);
                var row = new StepProgress(
                    index + 1, exercise.ExerciseId, evaluation.Steps[index], filters,
                    ExerciseFilterPresets.Describe(groups, filters));
                if (evaluation.JustCompleted == index) justCompleted = row;
                rows.Add(row);
                index++;
            }
            unitRows.Add(new UnitProgress(unit.Key, unitRows.Count + 1, rows));
        }

        return new LearningPathProgress(unitRows, justCompleted);
    }
}
