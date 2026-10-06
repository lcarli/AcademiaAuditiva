using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

/// <summary>
/// The teacher's reports (<see cref="RoutineReports"/>): an assignment of one of their routines,
/// one of their classrooms, or a student in one of them. Anything else is not found.
/// </summary>
public class DashboardController : TeacherAreaController
{
    private readonly RoutineReports _reports;
    private readonly UserManager<ApplicationUser> _users;

    public DashboardController(RoutineReports reports, UserManager<ApplicationUser> users)
    {
        _reports = reports;
        _users = users;
    }

    private string TeacherId => _users.GetUserId(User)!;

    // Some sidebar/menu links point at /Teacher/Dashboard. The actual teacher
    // landing page is rendered by HomeController, so redirect there instead
    // of 404'ing.
    public IActionResult Index() => RedirectToAction("Index", "Home");

    public async Task<IActionResult> Assignment(int id)
    {
        var report = await _reports.AssignmentAsync(TeacherId, id, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
        return report is null ? NotFound() : View(report);
    }

    public async Task<IActionResult> Classroom(int id)
    {
        var report = await _reports.ClassroomAsync(TeacherId, id, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
        return report is null ? NotFound() : View(report);
    }

    /// <param name="classroomId">The classroom report the teacher came from, if any.</param>
    /// <param name="assignmentId">The routine report the teacher came from, if any.</param>
    public async Task<IActionResult> Student(string id, int? classroomId = null, int? assignmentId = null)
    {
        var report = await _reports.StudentAsync(TeacherId, id, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
        if (report is null) return NotFound();

        var backToAssignment = report.Routines.Any(r => r.Assignment.Id == assignmentId) ? assignmentId : null;
        var backToClassroom = backToAssignment is null && report.Classrooms.Any(c => c.Id == classroomId) ? classroomId : null;
        return View(new StudentReportPage(report, backToAssignment, backToClassroom));
    }
}
