using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Routines;
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
    private readonly RoutineRounds _routines;

    public MyTrainingController(ApplicationDbContext db, UserManager<ApplicationUser> users, RoutineRounds routines)
    {
        _db = db;
        _users = users;
        _routines = routines;
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

        ViewBag.Classrooms = classrooms;
        ViewBag.Assignments = await _routines.ListAsync(uid, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
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
