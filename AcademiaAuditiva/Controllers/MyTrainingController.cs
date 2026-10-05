using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Controllers;

/// <summary>
/// Student-facing views into the teaching domain: classrooms they belong to
/// and routines (own + classroom-wide) currently assigned to them.
/// </summary>
[Authorize]
public class MyTrainingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IStringLocalizer<SharedResources> _l;

    public MyTrainingController(ApplicationDbContext db, UserManager<ApplicationUser> users, IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _users = users;
        _l = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var uid = _users.GetUserId(User)!;

        var classrooms = await _db.ClassroomMembers
            .Where(m => m.StudentId == uid && !m.Classroom!.IsArchived)
            .Include(m => m.Classroom).ThenInclude(c => c!.Owner)
            .OrderBy(m => m.Classroom!.Name)
            .Select(m => new MyClassroomRow
            {
                ClassroomId = m.ClassroomId,
                Name = m.Classroom!.Name,
                TeacherDisplay = m.Classroom.Owner != null
                    ? (m.Classroom.Owner.UserName ?? m.Classroom.Owner.Email ?? "")
                    : "",
                JoinedAt = m.JoinedAt
            })
            .ToListAsync();

        var classroomIds = classrooms.Select(c => c.ClassroomId).ToList();

        var assignments = await _db.RoutineAssignments
            .Where(a =>
                a.StudentId == uid ||
                (a.ClassroomId != null && classroomIds.Contains(a.ClassroomId.Value)))
            .Include(a => a.Routine!).ThenInclude(r => r!.Items).ThenInclude(i => i.Exercise)
            .Include(a => a.Classroom)
            .OrderBy(a => a.DueAt ?? DateTime.MaxValue)
            .ThenBy(a => a.Id) // stable order for assignments without a due date
            .ToListAsync();

        // Load per-student overrides for the classroom-wide assignments.
        var assignmentIds = assignments.Where(a => a.ClassroomId != null).Select(a => a.Id).ToList();
        var overrides = assignmentIds.Count == 0
            ? new List<RoutineAssignmentOverride>()
            : await _db.RoutineAssignmentOverrides
                .Where(o => assignmentIds.Contains(o.RoutineAssignmentId) && o.StudentId == uid)
                .ToListAsync();

        // Progress counts the attempts made since each routine was assigned,
        // per exercise, whatever filters they were played with (older attempts
        // don't record them), so an item's preset is guidance, not a constraint
        // on what counts.
        var routineExerciseIds = assignments
            .SelectMany(a => a.Routine!.Items.Select(i => i.ExerciseId))
            .Distinct()
            .ToList();
        var since = assignments.Count == 0 ? DateTime.MaxValue : assignments.Min(a => a.AssignedAt);

        var attempts = routineExerciseIds.Count == 0
            ? new List<AttemptRow>()
            : await _db.ScoreSnapshots
                .Where(s => s.UserId == uid && routineExerciseIds.Contains(s.ExerciseId) && s.Timestamp >= since)
                .Select(s => new AttemptRow(s.ExerciseId, s.IsCorrect, s.Timestamp))
                .ToListAsync();

        var rows = assignments.Select(a =>
        {
            var items = a.Routine!.Items.OrderBy(i => i.Order).Select(i =>
            {
                var ovr = overrides.FirstOrDefault(o =>
                    o.RoutineAssignmentId == a.Id && o.RoutineItemId == i.Id);
                var effective = RoutineItemResolver.Resolve(i, ovr);
                if (effective is null) return null;

                var groups = ExerciseFilterPresets.Groups(i.Exercise?.FiltersJson);
                // Only known groups/options reach the URL, so a preset can never
                // set route values such as "controller" or "area".
                var filters = ExerciseFilterPresets.Sanitize(
                    effective.Value.Filters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
                    groups);
                var mine = attempts.Where(s => s.ExerciseId == i.ExerciseId && s.Timestamp >= a.AssignedAt).ToList();
                var exerciseName = i.Exercise?.Name ?? string.Empty;
                var localizedName = _l[exerciseName];

                return new MyRoutineItemRow
                {
                    ItemId = i.Id,
                    ExerciseId = i.ExerciseId,
                    ExerciseName = exerciseName,
                    DisplayName = localizedName.ResourceNotFound ? exerciseName : localizedName.Value,
                    Filters = filters,
                    AppliedFilters = ExerciseFilterPresets.Describe(groups, filters),
                    Progress = new RoutineItemProgress(
                        Attempts: mine.Count,
                        Correct: mine.Count(s => s.IsCorrect),
                        Target: effective.Value.Target,
                        MinScore: effective.Value.MinScore)
                };
            })
            .Where(x => x != null)
            .Cast<MyRoutineItemRow>()
            .ToList();

            return new MyRoutineAssignmentRow
            {
                AssignmentId = a.Id,
                RoutineName = a.Routine!.Name,
                ClassroomName = a.ClassroomId != null ? a.Classroom?.Name ?? string.Empty : null,
                AssignedAt = a.AssignedAt,
                DueAt = a.DueAt,
                Items = items,
                OverallPercent = items.Count == 0 ? 0
                    : (int)Math.Floor(items.Average(x => (double)x.Progress.Percent))
            };
        }).ToList();

        ViewBag.Classrooms = classrooms;
        ViewBag.Assignments = rows;
        return View();
    }
}

public class MyClassroomRow
{
    public int ClassroomId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TeacherDisplay { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class MyRoutineAssignmentRow
{
    public int AssignmentId { get; set; }
    public string RoutineName { get; set; } = string.Empty;
    /// <summary>Set for classroom-wide assignments; null for personal ones.</summary>
    public string? ClassroomName { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int OverallPercent { get; set; }
    public IReadOnlyList<MyRoutineItemRow> Items { get; set; } = Array.Empty<MyRoutineItemRow>();
}

public class MyRoutineItemRow
{
    public int ItemId { get; set; }
    public int ExerciseId { get; set; }
    /// <summary>Exercise key, which is also the <c>ExerciseController</c> action name.</summary>
    public string ExerciseName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Sanitized filter preset, passed to the exercise page as query values.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<AppliedFilter> AppliedFilters { get; set; } = Array.Empty<AppliedFilter>();
    public RoutineItemProgress Progress { get; set; }
}

internal sealed record AttemptRow(int ExerciseId, bool IsCorrect, DateTime Timestamp);
