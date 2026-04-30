using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Admin.Controllers;

public class HomeController : AdminAreaController
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
        ViewBag.UserCount = await _users.Users.CountAsync();
        ViewBag.TeacherCount = (await _users.GetUsersInRoleAsync(RoleNames.Teacher)).Count;
        ViewBag.AdminCount = (await _users.GetUsersInRoleAsync(RoleNames.Admin)).Count;
        ViewBag.StudentCount = (await _users.GetUsersInRoleAsync(RoleNames.Student)).Count;
        ViewBag.ClassroomCount = await _db.Classrooms.CountAsync();
        ViewBag.RoutineCount = await _db.Routines.CountAsync();
        return View();
    }
}
