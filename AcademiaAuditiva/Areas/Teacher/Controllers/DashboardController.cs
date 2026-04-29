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

        var scores = await _db.Scores
            .Where(s => studentIds.Contains(s.UserId))
            .ToListAsync();

        var perStudent = classroom.Members.Select(m =>
        {
            var mine = scores.Where(s => s.UserId == m.StudentId).ToList();
            var totalAttempts = mine.Sum(x => x.CorrectCount + x.ErrorCount);
            var accuracy = totalAttempts == 0 ? 0
                : Math.Round(100.0 * mine.Sum(x => x.CorrectCount) / totalAttempts, 1);
            return new ClassroomDashboardRow
            {
                StudentId = m.StudentId,
                Display = m.Student?.UserName ?? m.Student?.Email ?? m.StudentId,
                Email = m.Student?.Email ?? "",
                Sessions = mine.Count,
                Attempts = totalAttempts,
                Accuracy = accuracy,
                LastActivity = mine.OrderByDescending(x => x.Timestamp).FirstOrDefault()?.Timestamp,
                ActiveLast30Days = mine.Any(x => x.Timestamp >= since30)
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

        var scores = await _db.Scores
            .Where(s => s.UserId == id)
            .Include(s => s.Exercise)
            .ToListAsync();

        var totalAttempts = scores.Sum(s => s.CorrectCount + s.ErrorCount);
        var byExercise = scores
            .GroupBy(s => new { s.ExerciseId, Name = s.Exercise?.Name ?? "?" })
            .Select(g =>
            {
                var att = g.Sum(x => x.CorrectCount + x.ErrorCount);
                return new StudentExerciseRow
                {
                    ExerciseName = g.Key.Name,
                    Attempts = att,
                    Accuracy = att == 0 ? 0 : Math.Round(100.0 * g.Sum(x => x.CorrectCount) / att, 1),
                    BestScore = g.Max(x => x.BestScore),
                    LastActivity = g.Max(x => x.Timestamp)
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
            TotalSessions = scores.Count,
            TotalAttempts = totalAttempts,
            OverallAccuracy = totalAttempts == 0 ? 0
                : Math.Round(100.0 * scores.Sum(s => s.CorrectCount) / totalAttempts, 1),
            ActiveLast30Days = scores.Any(s => s.Timestamp >= since30),
            LastActivity = scores.OrderByDescending(s => s.Timestamp).FirstOrDefault()?.Timestamp,
            ByExercise = byExercise
        };
        return View(vm);
    }
}
