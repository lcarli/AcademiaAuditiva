using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Controllers;

[AllowAnonymous]
[Route("invite")]
public class InvitesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ILogger<InvitesController> _logger;

    public InvitesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ILogger<InvitesController> logger)
    {
        _db = db;
        _users = users;
        _signIn = signIn;
        _logger = logger;
    }

    /// <summary>
    /// Public landing for invite links. Three branches:
    /// 1. Token invalid/expired/already accepted → friendly error.
    /// 2. User signed in → consume invite (assign Student role + add membership) and redirect.
    /// 3. Anonymous → redirect to Register pre-filled with the invite email; we keep the
    ///    token in TempData and finish the flow on the next GET to /invite/accept after login.
    /// </summary>
    [HttpGet("accept")]
    public async Task<IActionResult> Accept(string token, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return View("Invalid");
        }

        var invite = await _db.ClassroomInvites
            .Include(i => i.Classroom)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (!InvitePolicy.IsAcceptable(invite, DateTime.UtcNow))
        {
            return View("Invalid");
        }

        if (!_signIn.IsSignedIn(User))
        {
            // After registration/login the returnUrl brings the user back to Accept
            // signed in, where the membership branch below runs.
            var registerUrl = Url.Page("/Account/Register", new
            {
                area = "Identity",
                returnUrl = Url.Action(nameof(Accept), new { token }),
                email = invite.Email
            });
            return Redirect(registerUrl ?? "/Identity/Account/Register");
        }

        var userId = _users.GetUserId(User)!;
        var user = (await _users.FindByIdAsync(userId))!;

        // Email mismatch → require re-login as the right user, but don't block silently.
        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.ExpectedEmail = invite.Email;
            ViewBag.ActualEmail = user.Email;
            return View("EmailMismatch");
        }

        // Make sure the user has the Student role so they can practice.
        if (!await _users.IsInRoleAsync(user, RoleNames.Student))
        {
            await _users.AddToRoleAsync(user, RoleNames.Student);
        }

        var alreadyMember = await _db.ClassroomMembers
            .AnyAsync(m => m.ClassroomId == invite.ClassroomId && m.StudentId == user.Id);

        if (!alreadyMember)
        {
            _db.ClassroomMembers.Add(new ClassroomMember
            {
                ClassroomId = invite.ClassroomId,
                StudentId = user.Id,
                JoinedAt = DateTime.UtcNow
            });
        }

        invite.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Refresh the auth cookie so the new role takes effect immediately.
        await _signIn.RefreshSignInAsync(user);

        _logger.LogInformation("User {UserId} accepted invite {InviteId} for classroom {ClassroomId}",
            user.Id, invite.Id, invite.ClassroomId);

        TempData["Success"] = $"You have joined {invite.Classroom?.Name}.";
        return RedirectToAction("Index", "Dashboard");
    }
}
