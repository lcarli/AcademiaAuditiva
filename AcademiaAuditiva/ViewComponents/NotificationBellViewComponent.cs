using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.ViewComponents;

/// <summary>The header's bell: opens the notifications and shows how many are unread.</summary>
public sealed class NotificationBellViewComponent : ViewComponent
{
    private readonly NotificationInbox _inbox;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<NotificationBellViewComponent> _logger;

    public NotificationBellViewComponent(
        NotificationInbox inbox,
        UserManager<ApplicationUser> users,
        ILogger<NotificationBellViewComponent> logger)
    {
        _inbox = inbox;
        _users = users;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = _users.GetUserId(UserClaimsPrincipal);
        if (userId is null) return Content(string.Empty);

        var unread = 0;
        try
        {
            unread = await _inbox.UnreadCountAsync(userId, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The bell still opens the notifications.
            _logger.LogWarning(ex, "Could not count the unread notifications.");
        }
        return View(unread);
    }
}
