using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Controllers;

/// <summary>The page the header's bell opens: the user's notifications, newest first.</summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly NotificationInbox _inbox;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IStringLocalizer<SharedResources> _l;

    public NotificationsController(NotificationInbox inbox, UserManager<ApplicationUser> users, IStringLocalizer<SharedResources> localizer)
    {
        _inbox = inbox;
        _users = users;
        _l = localizer;
    }

    private string UserId => _users.GetUserId(User)!;

    public async Task<IActionResult> Index()
    {
        var userId = UserId;
        ViewBag.Unread = await _inbox.UnreadCountAsync(userId, HttpContext.RequestAborted);
        ViewBag.TimeZone = UserTimeZone.FromRequest(Request);
        return View(await _inbox.ListAsync(userId, HttpContext.RequestAborted));
    }

    /// <summary>Marks the notification read and opens what it is about.</summary>
    public async Task<IActionResult> Open(int id)
    {
        var notification = await _inbox.OpenAsync(UserId, id, HttpContext.RequestAborted);
        return notification switch
        {
            null => NotFound(),
            { Kind: NotificationKind.RoutineAssigned or NotificationKind.RoutineDueTomorrow } =>
                RedirectToAction("Index", "MyTraining", new { area = "" }, $"routine-{notification.RoutineAssignmentId}"),
            { Kind: NotificationKind.StudentFinishedRoutine } =>
                RedirectToAction("Student", "Dashboard", new { area = "Teacher", id = notification.StudentId, assignmentId = notification.RoutineAssignmentId }),
            { Kind: NotificationKind.InviteAccepted } =>
                RedirectToAction("Details", "Classrooms", new { area = "Teacher", id = notification.ClassroomId }),
            _ => RedirectToAction(nameof(Index)),
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await _inbox.MarkAllReadAsync(UserId, HttpContext.RequestAborted);
        TempData["Success"] = _l["Toast.NotificationsRead"].Value;
        return RedirectToAction(nameof(Index));
    }
}
