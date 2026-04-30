using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class DashboardController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private string TeacherId => _users.GetUserId(User)!;

    public async Task<IActionResult> Classroom(int id)
    {
        var classroom = await _db.Classrooms
            .Include(c => c.Members).ThenInclude(m => m.Student)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);
        if (classroom == null) return NotFound();

        var studentIds = classroom.Members.Select(m => m.StudentId).ToList();
        var since30 = DateTime.UtcNow.AddDays(-30);

        // Use ScoreAggregates for cumulative totals (one row per user/exercise),
        // and ScoreSnapshots for per-attempt counts. The legacy `Scores` table
        // stores running totals across rows so summing it overcounts attempts.
        var aggregates = await _db.ScoreAggregates
            .Where(a => studentIds.Contains(a.UserId))
            .ToListAsync();

        var snapshotCounts = await _db.ScoreSnapshots
            .Where(s => studentIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Sessions = g.Count() })
            .ToListAsync();

        var perStudent = classroom.Members.Select(m =>
        {
            var mineAgg = aggregates.Where(a => a.UserId == m.StudentId).ToList();
            var totalAttempts = mineAgg.Sum(a => a.CorrectCount + a.ErrorCount);
            var totalCorrect = mineAgg.Sum(a => a.CorrectCount);
            var accuracy = totalAttempts == 0 ? 0
                : Math.Round(100.0 * totalCorrect / totalAttempts, 1);
            var lastActivity = mineAgg.Count == 0 ? (DateTime?)null
                : mineAgg.Max(a => a.LastAttemptAt);
            return new ClassroomDashboardRow
            {
                StudentId = m.StudentId,
                Display = m.Student?.UserName ?? m.Student?.Email ?? m.StudentId,
                Email = m.Student?.Email ?? "",
                Sessions = snapshotCounts.FirstOrDefault(c => c.UserId == m.StudentId)?.Sessions ?? 0,
                Attempts = totalAttempts,
                Accuracy = accuracy,
                LastActivity = lastActivity,
                ActiveLast30Days = lastActivity.HasValue && lastActivity.Value >= since30
            };
        })
        .OrderByDescending(r => r.LastActivity ?? DateTime.MinValue)
        .ToList();

        var vm = new ClassroomDashboardViewModel
        {
            ClassroomId = classroom.Id,
            ClassroomName = classroom.Name,
            Rows = perStudent,
            TotalAttempts = perStudent.Sum(r => r.Attempts),
            TotalSessions = perStudent.Sum(r => r.Sessions),
            AverageAccuracy = perStudent.Where(r => r.Attempts > 0).Select(r => r.Accuracy).DefaultIfEmpty(0).Average(),
            ActiveStudentsLast30Days = perStudent.Count(r => r.ActiveLast30Days)
        };
        return View(vm);
    }

    public async Task<IActionResult> Student(string id, int? classroomId = null)
    {
        // The teacher can only see students that are members of one of their classrooms.
        var membership = await _db.ClassroomMembers
            .Include(m => m.Classroom)
            .Include(m => m.Student)
            .Where(m => m.StudentId == id && m.Classroom!.OwnerId == TeacherId)
            .ToListAsync();
        if (membership.Count == 0) return Forbid();

        var student = membership.First().Student!;
        var since30 = DateTime.UtcNow.AddDays(-30);

        var aggregates = await _db.ScoreAggregates
            .Where(a => a.UserId == id)
            .Include(a => a.Exercise)
            .ToListAsync();

        var sessionCount = await _db.ScoreSnapshots.CountAsync(s => s.UserId == id);

        var totalAttempts = aggregates.Sum(a => a.CorrectCount + a.ErrorCount);
        var totalCorrect = aggregates.Sum(a => a.CorrectCount);
        var lastActivity = aggregates.Count == 0 ? (DateTime?)null
            : aggregates.Max(a => a.LastAttemptAt);

        var byExercise = aggregates
            .Select(a =>
            {
                var att = a.CorrectCount + a.ErrorCount;
                return new StudentExerciseRow
                {
                    ExerciseName = a.Exercise?.Name ?? "?",
                    Attempts = att,
                    Accuracy = att == 0 ? 0 : Math.Round(100.0 * a.CorrectCount / att, 1),
                    BestScore = a.BestScore,
                    LastActivity = a.LastAttemptAt
                };
            })
            .OrderByDescending(r => r.LastActivity)
            .ToList();

        var vm = new StudentDashboardViewModel
        {
            StudentId = id,
            Display = student.UserName ?? student.Email ?? id,
            Email = student.Email ?? "",
            Classrooms = membership.Select(m => new ClassroomOption(m.ClassroomId, m.Classroom!.Name)).ToList(),
            BackClassroomId = classroomId,
            TotalSessions = sessionCount,
            TotalAttempts = totalAttempts,
            OverallAccuracy = totalAttempts == 0 ? 0
                : Math.Round(100.0 * totalCorrect / totalAttempts, 1),
            ActiveLast30Days = lastActivity.HasValue && lastActivity.Value >= since30,
            LastActivity = lastActivity,
            ByExercise = byExercise
        };
        return View(vm);
    }
}
