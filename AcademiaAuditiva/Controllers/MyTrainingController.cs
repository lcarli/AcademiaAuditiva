using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    public MyTrainingController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
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
            .ToListAsync();

        // Load per-student overrides for the classroom-wide assignments.
        var assignmentIds = assignments.Where(a => a.ClassroomId != null).Select(a => a.Id).ToList();
        var overrides = assignmentIds.Count == 0
            ? new List<RoutineAssignmentOverride>()
            : await _db.RoutineAssignmentOverrides
                .Where(o => assignmentIds.Contains(o.RoutineAssignmentId) && o.StudentId == uid)
                .ToListAsync();

        // Build per-assignment progress using best-effort match via ExerciseId.
        var routineExerciseIds = assignments
            .SelectMany(a => a.Routine!.Items.Select(i => i.ExerciseId))
            .Distinct()
            .ToList();

        var scores = await _db.Scores
            .Where(s => s.UserId == uid && routineExerciseIds.Contains(s.ExerciseId))
            .ToListAsync();

        var rows = assignments.Select(a =>
        {
            var items = a.Routine!.Items.OrderBy(i => i.Order).Select(i =>
            {
                var ovr = overrides.FirstOrDefault(o =>
                    o.RoutineAssignmentId == a.Id && o.RoutineItemId == i.Id);
                var effective = RoutineItemResolver.Resolve(i, ovr);
                if (effective is null) return null;

                var target = effective.Value.Target;
                var done = scores.Where(s => s.ExerciseId == i.ExerciseId)
                    .Sum(s => s.CorrectCount + s.ErrorCount);
                var clamped = Math.Min(done, target);
                return new MyRoutineItemRow
                {
                    ItemId = i.Id,
                    ExerciseId = i.ExerciseId,
                    ExerciseName = i.Exercise?.Name ?? "",
                    Target = target,
                    Done = clamped,
                    PercentComplete = target == 0 ? 100 : (int)Math.Round(100.0 * clamped / target)
                };
            })
            .Where(x => x != null)
            .Cast<MyRoutineItemRow>()
            .ToList();

            return new MyRoutineAssignmentRow
            {
                AssignmentId = a.Id,
                RoutineName = a.Routine!.Name,
                Source = a.ClassroomId != null
                    ? $"Classroom: {a.Classroom?.Name}"
                    : "Personal",
                AssignedAt = a.AssignedAt,
                DueAt = a.DueAt,
                Items = items,
                OverallPercent = items.Count == 0 ? 0
                    : (int)Math.Round(items.Average(x => (double)x.PercentComplete))
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
    public string Source { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int OverallPercent { get; set; }
    public IReadOnlyList<MyRoutineItemRow> Items { get; set; } = Array.Empty<MyRoutineItemRow>();
}

public class MyRoutineItemRow
{
    public int ItemId { get; set; }
    public int ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Done { get; set; }
    public int PercentComplete { get; set; }
}
