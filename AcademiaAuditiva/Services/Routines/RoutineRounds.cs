using System.Text.Json;
using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>
/// Routines are taken like a test. Each item asks its exercise as many questions as its
/// target, with the teacher's filters; every answer counts and nothing past the target is
/// taken. Routine answers are tagged with their assignment, item and question number
/// (<see cref="Models.ScoreSnapshot.RoutineAssignmentId"/>) and progress counts only them,
/// so practice outside the routine never fills it. A unique index on the tag keeps two
/// answers from taking the same question.
///
/// Until it is answered, an item's current question is served again on every Play
/// (<see cref="PendingQuestionAsync"/>), so a hard one cannot be skipped by asking for another;
/// once it is, a window still showing it is told so when it answers (<see cref="RememberAnsweredAsync"/>).
/// </summary>
public sealed class RoutineRounds
{
    // As long as the round itself (AudioTokenService).
    private static readonly DistributedCacheEntryOptions PendingTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

    private readonly ApplicationDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _clock;

    public RoutineRounds(ApplicationDbContext db, IDistributedCache cache, TimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    /// <summary>The student's routines, the soonest due first, each with its items in order.</summary>
    public async Task<IReadOnlyList<AssignedRoutine>> ListAsync(
        string userId, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var assignments = await WithItems(Visible(userId))
            .OrderBy(a => a.DueAt ?? DateTime.MaxValue)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return await BuildAsync(userId, assignments, timeZone, cancellationToken);
    }

    /// <summary>
    /// One item of one of the student's routines; null when the routine is not (or no longer)
    /// assigned to them, or the item is not in it or is excluded for them.
    /// </summary>
    public async Task<RoutineRoundContext?> FindAsync(
        string userId, RoutineLink link, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var assignment = await WithItems(Visible(userId).Where(a => a.Id == link.AssignmentId))
            .FirstOrDefaultAsync(cancellationToken);
        if (assignment is null) return null;

        var routine = (await BuildAsync(userId, [assignment], timeZone, cancellationToken))[0];
        var item = routine.Items.FirstOrDefault(i => i.ItemId == link.ItemId);
        return item is null ? null : new RoutineRoundContext(routine, item);
    }

    /// <summary>The item's question that was played but not answered yet, if any.</summary>
    public async Task<PendingQuestion?> PendingQuestionAsync(
        string userId, RoutineLink link, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(PendingKey(userId, link), cancellationToken);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingQuestion>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task RememberQuestionAsync(
        string userId, RoutineLink link, PendingQuestion question, CancellationToken cancellationToken = default)
        => _cache.SetStringAsync(PendingKey(userId, link), JsonSerializer.Serialize(question), PendingTtl, cancellationToken);

    /// <summary>
    /// Remembers, for as long as the round would have lasted, that the round asking
    /// <paramref name="question"/> was answered: another window showing the same round is then
    /// told so when it answers, rather than that its round expired.
    /// </summary>
    public Task RememberAnsweredAsync(
        string userId, int exerciseId, string roundId, RoutineQuestion question, CancellationToken cancellationToken = default)
        => _cache.SetStringAsync(
            AnsweredKey(userId, exerciseId, roundId),
            JsonSerializer.Serialize(new AnsweredRound(question.Link.AssignmentId, question.Link.ItemId, question.Number)),
            PendingTtl,
            cancellationToken);

    /// <summary>The routine question an answered round asked (<see cref="RememberAnsweredAsync"/>), if any.</summary>
    public async Task<RoutineQuestion?> AnsweredQuestionAsync(
        string userId, int exerciseId, string roundId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(AnsweredKey(userId, exerciseId, roundId), cancellationToken);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var round = JsonSerializer.Deserialize<AnsweredRound>(json);
            return round is null ? null : RoutineQuestion.From(round.AssignmentId, round.ItemId, round.Number);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string PendingKey(string userId, RoutineLink link)
        => $"RoutineRound:{userId}:{link.AssignmentId}:{link.ItemId}";

    private static string AnsweredKey(string userId, int exerciseId, string roundId)
        => $"RoutineAnswered:{userId}:{exerciseId}:{roundId}";

    private sealed record AnsweredRound(int AssignmentId, int ItemId, int Number);

    // Assigned to the student, or to a classroom they are in that is not archived: to the whole
    // class, or to some of its students, them included.
    private IQueryable<RoutineAssignment> Visible(string userId)
        => _db.RoutineAssignments.Where(a =>
            a.StudentId == userId ||
            (a.ClassroomId != null
                && _db.ClassroomMembers.Any(m => m.ClassroomId == a.ClassroomId && m.StudentId == userId && !m.Classroom!.IsArchived)
                && (!a.ChosenStudentsOnly || a.ChosenStudents.Any(s => s.StudentId == userId))));

    private static IQueryable<RoutineAssignment> WithItems(IQueryable<RoutineAssignment> assignments)
        => assignments.AsNoTracking()
            .Include(a => a.Routine!).ThenInclude(r => r.Items).ThenInclude(i => i.Exercise)
            .Include(a => a.Classroom);

    private async Task<List<AssignedRoutine>> BuildAsync(
        string userId, IReadOnlyList<RoutineAssignment> assignments, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0) return [];

        var ids = assignments.Select(a => a.Id).ToList();
        var overrides = await _db.RoutineAssignmentOverrides.AsNoTracking()
            .Where(o => o.StudentId == userId && ids.Contains(o.RoutineAssignmentId))
            .ToListAsync(cancellationToken);
        var answers = await _db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.UserId == userId && s.RoutineAssignmentId != null && ids.Contains(s.RoutineAssignmentId.Value))
            .GroupBy(s => new { s.RoutineAssignmentId, s.RoutineItemId })
            .Select(g => new
            {
                g.Key.RoutineAssignmentId,
                g.Key.RoutineItemId,
                Attempts = g.Count(),
                Correct = g.Sum(s => s.IsCorrect ? 1 : 0)
            })
            .ToListAsync(cancellationToken);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var routines = new List<AssignedRoutine>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var window = RoutineSchedule.Window(assignment.DueAt, assignment.AllowLate, nowUtc, timeZone);
            var items = new List<AssignedRoutineItem>();
            foreach (var item in assignment.Routine!.Items.OrderBy(i => i.Order).ThenBy(i => i.Id))
            {
                var @override = overrides.FirstOrDefault(o =>
                    o.RoutineAssignmentId == assignment.Id && o.RoutineItemId == item.Id);
                if (RoutineItemResolver.Resolve(item, @override) is not { } effective) continue;

                var groups = ExerciseFilterPresets.Groups(item.Exercise?.FiltersJson);
                // Only known groups and options reach the exercise page's URL, so a preset
                // can never set route values such as "controller" or "area".
                var filters = ExerciseFilterPresets.Sanitize(
                    effective.Filters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
                    groups);
                var answered = answers.FirstOrDefault(c =>
                    c.RoutineAssignmentId == assignment.Id && c.RoutineItemId == item.Id);

                items.Add(new AssignedRoutineItem(
                    AssignmentId: assignment.Id,
                    ItemId: item.Id,
                    ExerciseId: item.ExerciseId,
                    ExerciseName: item.Exercise?.Name ?? string.Empty,
                    Filters: filters,
                    AppliedFilters: ExerciseFilterPresets.Describe(groups, filters),
                    Progress: new RoutineItemProgress(
                        Attempts: answered?.Attempts ?? 0,
                        Correct: answered?.Correct ?? 0,
                        Target: effective.Target,
                        MinScore: effective.MinScore),
                    Window: window));
            }

            routines.Add(new AssignedRoutine(
                AssignmentId: assignment.Id,
                RoutineName: assignment.Routine.Name,
                ClassroomName: assignment.ClassroomId != null ? assignment.Classroom?.Name ?? string.Empty : null,
                AssignedAt: assignment.AssignedAt,
                DueAt: assignment.DueAt,
                AllowLate: assignment.AllowLate,
                Window: window,
                Items: items));
        }
        return routines;
    }
}

/// <summary>A routine assigned to the student, to a classroom of theirs or to them alone.</summary>
/// <param name="ClassroomName">Set for classroom assignments; null for personal ones.</param>
/// <param name="DueAt">The due date (no time), see <see cref="RoutineSchedule"/>.</param>
/// <param name="Items">The items in order, without those excluded for the student.</param>
public sealed record AssignedRoutine(
    int AssignmentId,
    string RoutineName,
    string? ClassroomName,
    DateTime AssignedAt,
    DateTime? DueAt,
    bool AllowLate,
    RoutineWindow Window,
    IReadOnlyList<AssignedRoutineItem> Items)
{
    /// <summary>Every question of every item is answered.</summary>
    public bool IsFinished => Items.Count > 0 && Items.All(i => i.Progress.IsComplete);

    /// <summary>The items' average <see cref="RoutineItemProgress.Percent"/>, rounded down.</summary>
    public int Percent => Items.Count == 0 ? 0 : (int)Math.Floor(Items.Average(i => (double)i.Progress.Percent));
}

/// <summary>One item of an <see cref="AssignedRoutine"/>, once the student's override is applied.</summary>
/// <param name="ExerciseName">The exercise key, which is also its <c>ExerciseController</c> action.</param>
/// <param name="Filters">The teacher's filter preset, sanitized; the other filters are the student's choice.</param>
/// <param name="Progress">Counted from the item's tagged answers only.</param>
public sealed record AssignedRoutineItem(
    int AssignmentId,
    int ItemId,
    int ExerciseId,
    string ExerciseName,
    IReadOnlyDictionary<string, string> Filters,
    IReadOnlyList<AppliedFilter> AppliedFilters,
    RoutineItemProgress Progress,
    RoutineWindow Window)
{
    public RoutineLink Link => new(AssignmentId, ItemId);

    /// <summary>Takes answers: some questions are left and the routine is not closed.</summary>
    public bool IsPlayable => !Progress.IsComplete && Window != RoutineWindow.Closed;

    /// <summary>The question the next answer takes (<see cref="Models.ScoreSnapshot.RoutineQuestion"/>).</summary>
    public int NextQuestion => Progress.Attempts + 1;

    /// <summary>The exercise page's query: the teacher's filters and the routine link.</summary>
    public RouteValueDictionary RouteValues()
    {
        var values = new RouteValueDictionary();
        foreach (var (group, option) in Filters) values[group] = option;
        values[RoutineLink.AssignmentKey] = AssignmentId;
        values[RoutineLink.ItemKey] = ItemId;
        return values;
    }

    /// <summary>The item once one more answer is counted.</summary>
    public AssignedRoutineItem Answered(bool isCorrect) => this with
    {
        Progress = Progress with
        {
            Attempts = Progress.Attempts + 1,
            Correct = Progress.Correct + (isCorrect ? 1 : 0)
        }
    };
}

public sealed record RoutineRoundContext(AssignedRoutine Routine, AssignedRoutineItem Item);

/// <summary>
/// A routine question that was played but not answered yet: its audio round or, for an exercise
/// shown as sheet music (which has no round), the session its answer is checked against. The
/// session is kept here because free practice of the same exercise reuses its cache entry.
/// </summary>
public sealed record PendingQuestion(string? RoundId = null, ExerciseSessionData? Sheet = null);
