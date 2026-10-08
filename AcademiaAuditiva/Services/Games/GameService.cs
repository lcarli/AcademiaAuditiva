using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.Games;

/// <summary>Where a run stands after an answer (or when it is looked at).</summary>
/// <param name="Placement">The placement test's state; null for the other modes.</param>
/// <param name="Best">The player's best score in the mode and exercise before this run; null for none.</param>
public sealed record GameProgress(GameRun Run, bool Over, PlacementState? Placement, int? Best)
{
    public bool NewBest => Over && Run.Score > 0 && Run.Score > (Best ?? 0)
        && GameModes.RecordModes.Contains(Run.Mode);
}

/// <summary>The best score of a mode and exercise.</summary>
public sealed record GameRecord(string Mode, int ExerciseId, string Exercise, int Best, int Runs);

/// <summary>A weak spot with the exercise and settings to open it with.</summary>
public sealed record WeakSpotLink(WeakSpot Spot, string Exercise, IReadOnlyDictionary<string, string> Filters,
    IReadOnlyList<AppliedFilter> AppliedFilters);

/// <summary>An exercise of the placement test with its preset, sanitized for its page.</summary>
public sealed record PlacementItemLink(PlacementItem Item, int ExerciseId, IReadOnlyDictionary<string, string> Filters);

/// <summary>
/// Game runs: starting and finishing them, and keeping their score. Answers are scored as
/// usual by ExerciseController.ValidateExercise, which tags them with the run, and the run's
/// <see cref="GameRun.Score"/> and <see cref="GameRun.Answered"/> are counted from those answers.
/// </summary>
public sealed class GameService
{
    private readonly ApplicationDbContext _db;
    private readonly PracticeHistory _history;

    public GameService(ApplicationDbContext db, PracticeHistory history)
    {
        _db = db;
        _history = history;
    }

    /// <summary>The player's run, tracked; null when there is none with that id.</summary>
    public Task<GameRun?> FindAsync(string userId, int runId, CancellationToken ct = default)
        => _db.GameRuns.FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct);

    public Task<string?> ExerciseNameAsync(int exerciseId, CancellationToken ct = default)
        => _db.Exercises.AsNoTracking()
            .Where(e => e.ExerciseId == exerciseId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(ct);

    /// <summary>Starts a sprint, survival or weak spot run on the exercise with the given settings.</summary>
    public async Task<GameRun> StartAsync(string userId, string mode, Exercise exercise, string? filterJson,
        DateTime now, CancellationToken ct = default)
    {
        if (!GameModes.IsExerciseMode(mode)) throw new ArgumentException($"Not an exercise mode: {mode}", nameof(mode));

        var run = new GameRun
        {
            UserId = userId,
            Mode = mode,
            ExerciseId = exercise.ExerciseId,
            FilterJson = filterJson,
            StartedAt = now,
        };
        _db.GameRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Starts a placement test, ending the player's unfinished one.</summary>
    public async Task<GameRun> StartPlacementAsync(string userId, DateTime now, CancellationToken ct = default)
    {
        var open = await _db.GameRuns
            .Where(r => r.UserId == userId && r.Mode == GameModes.Placement && r.EndedAt == null)
            .ToListAsync(ct);
        foreach (var run in open) run.EndedAt = now;

        var started = new GameRun { UserId = userId, Mode = GameModes.Placement, StartedAt = now };
        _db.GameRuns.Add(started);
        await _db.SaveChangesAsync(ct);
        return started;
    }

    /// <summary>
    /// Whether an answer to <paramref name="exercise"/> given at <paramref name="now"/> counts for
    /// the run: the run takes it and asks that exercise (the placement test, its current one).
    /// </summary>
    public async Task<bool> TakesAnswerAsync(GameRun run, Exercise exercise, DateTime now, CancellationToken ct = default)
    {
        if (!GameRules.TakesAnswer(run, now)) return false;
        if (run.Mode != GameModes.Placement) return run.ExerciseId == exercise.ExerciseId;

        var state = PlacementTest.Evaluate(await AnswersAsync(run, ct));
        return state.Current?.Exercise == exercise.Name;
    }

    /// <summary>
    /// Where the run stands. With <paramref name="recount"/>, its score is counted again from its
    /// answers (after one was saved), and it ends when its mode says so.
    /// </summary>
    public async Task<GameProgress> ProgressAsync(GameRun run, DateTime now, bool recount, CancellationToken ct = default)
    {
        PlacementState? placement = null;
        if (recount || run.Mode == GameModes.Placement)
        {
            var answers = await AnswersAsync(run, ct);
            if (recount)
            {
                run.Score = answers.Count(c => c);
                run.Answered = answers.Count;
            }
            if (run.Mode == GameModes.Placement)
                placement = PlacementTest.Evaluate(answers);
        }

        var over = (placement?.Finished ?? GameRules.IsOver(run, run.Score, run.Answered, now)) || !GameRules.IsOpen(run, now);
        if (recount && run.EndedAt is null && over)
        {
            var deadline = GameRules.Deadline(run);
            run.EndedAt = now < deadline ? now : deadline;
            if (placement?.Finished == true) run.PlacementUnit = placement.SuggestedUnit;
        }
        if (recount) await _db.SaveChangesAsync(ct);

        return new GameProgress(run, over, placement, await BestBeforeAsync(run, ct));
    }

    /// <summary>Ends the run (the sprint's time is up, or the player stopped).</summary>
    public async Task<GameProgress> FinishAsync(GameRun run, DateTime now, CancellationToken ct = default)
    {
        if (run.EndedAt is null && run.Mode != GameModes.Placement)
        {
            var deadline = GameRules.Deadline(run);
            run.EndedAt = now < deadline ? now : deadline;
            await _db.SaveChangesAsync(ct);
        }
        return await ProgressAsync(run, now, recount: false, ct);
    }

    /// <summary>The player's best score in each mode and exercise with records, best first.</summary>
    public async Task<IReadOnlyList<GameRecord>> RecordsAsync(string userId, CancellationToken ct = default)
    {
        var modes = GameModes.RecordModes.ToList();
        var rows = await _db.GameRuns.AsNoTracking()
            .Where(r => r.UserId == userId && modes.Contains(r.Mode) && r.ExerciseId != null && r.Score > 0)
            .GroupBy(r => new { r.Mode, r.ExerciseId })
            .Select(g => new { g.Key.Mode, ExerciseId = g.Key.ExerciseId!.Value, Best = g.Max(r => r.Score), Runs = g.Count() })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.ExerciseId).Distinct().ToList();
        var names = await _db.Exercises.AsNoTracking()
            .Where(e => ids.Contains(e.ExerciseId))
            .ToDictionaryAsync(e => e.ExerciseId, e => e.Name, ct);
        return rows
            .Where(r => names.ContainsKey(r.ExerciseId))
            .Select(r => new GameRecord(r.Mode, r.ExerciseId, names[r.ExerciseId], r.Best, r.Runs))
            .OrderBy(r => modes.IndexOf(r.Mode))
            .ThenByDescending(r => r.Best)
            .ThenBy(r => r.Exercise, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The exercises and settings the player most often gets wrong (<see cref="WeakSpotRules"/>).</summary>
    public async Task<IReadOnlyList<WeakSpotLink>> WeakSpotsAsync(string userId, CancellationToken ct = default)
    {
        var answers = await _history.GetAsync(userId, ct);
        if (answers.Count < WeakSpotRules.MinAnswers) return [];

        var exercises = await _db.Exercises.AsNoTracking()
            .Select(e => new { e.ExerciseId, e.Name, e.FiltersJson })
            .ToDictionaryAsync(e => e.ExerciseId, ct);
        var spots = WeakSpotRules.Find(answers,
            id => exercises.TryGetValue(id, out var e) && GameModes.Playable(e.Name));

        return spots.Select(spot =>
        {
            var exercise = exercises[spot.ExerciseId];
            var groups = ExerciseFilterPresets.Groups(exercise.FiltersJson);
            var filters = ExerciseFilterPresets.Sanitize(
                ExerciseFilterPresets.Parse(spot.FilterJson).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
                groups);
            return new WeakSpotLink(spot, exercise.Name, filters, ExerciseFilterPresets.Describe(groups, filters));
        }).ToList();
    }

    /// <summary>The player's latest placement test, or null.</summary>
    public Task<GameRun?> LatestPlacementAsync(string userId, CancellationToken ct = default)
        => _db.GameRuns.AsNoTracking()
            .Where(r => r.UserId == userId && r.Mode == GameModes.Placement)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The placement item's exercise and preset; null when the exercise is not seeded.</summary>
    public async Task<PlacementItemLink?> ItemLinkAsync(PlacementItem item, CancellationToken ct = default)
    {
        var exercise = await _db.Exercises.AsNoTracking()
            .Where(e => e.Name == item.Exercise)
            .OrderBy(e => e.ExerciseId)
            .Select(e => new { e.ExerciseId, e.FiltersJson })
            .FirstOrDefaultAsync(ct);
        if (exercise is null) return null;

        var filters = ExerciseFilterPresets.Sanitize(
            item.Step.Filters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
            ExerciseFilterPresets.Groups(exercise.FiltersJson));
        return new PlacementItemLink(item, exercise.ExerciseId, filters);
    }

    /// <summary>Whether every exercise of the placement test is seeded, so it can be taken.</summary>
    public async Task<bool> PlacementAvailableAsync(CancellationToken ct = default)
    {
        var names = PlacementTest.Items.Select(i => i.Exercise).Distinct().ToList();
        var seeded = await _db.Exercises.AsNoTracking()
            .Where(e => names.Contains(e.Name))
            .Select(e => e.Name)
            .Distinct()
            .CountAsync(ct);
        return seeded == names.Count;
    }

    /// <summary>Skips the learning path to the unit the finished test suggested.</summary>
    public async Task<bool> ApplyPlacementAsync(GameRun run, DateTime now, CancellationToken ct = default)
    {
        if (run.Mode != GameModes.Placement || run.PlacementUnit is not > 1) return false;
        if (run.AppliedAt is null)
        {
            run.AppliedAt = now;
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>Whether each answer of the run was right, oldest first.</summary>
    public async Task<List<bool>> AnswersAsync(GameRun run, CancellationToken ct = default)
        => await _db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.GameRunId == run.Id && s.UserId == run.UserId)
            .OrderBy(s => s.Timestamp).ThenBy(s => s.Id)
            .Select(s => s.IsCorrect)
            .ToListAsync(ct);

    private async Task<int?> BestBeforeAsync(GameRun run, CancellationToken ct)
    {
        if (!GameModes.RecordModes.Contains(run.Mode) || run.ExerciseId is null) return null;
        return await _db.GameRuns.AsNoTracking()
            .Where(r => r.UserId == run.UserId && r.Mode == run.Mode && r.ExerciseId == run.ExerciseId && r.Id != run.Id)
            .MaxAsync(r => (int?)r.Score, ct);
    }
}
