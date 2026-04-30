using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class HomeController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public HomeController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var teacherId = _users.GetUserId(User)!;
        var classroomCount = await _db.Classrooms.CountAsync(c => c.OwnerId == teacherId && !c.IsArchived);
        var routineCount = await _db.Routines.CountAsync(r => r.OwnerId == teacherId);
        var pendingInvites = await _db.ClassroomInvites
            .CountAsync(i => i.AcceptedAt == null && i.Classroom!.OwnerId == teacherId && i.ExpiresAt > DateTime.UtcNow);

        return View(new TeacherHomeViewModel
        {
            ActiveClassrooms = classroomCount,
            Routines = routineCount,
            PendingInvites = pendingInvites
        });
    }
}
