using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class MembersController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailMessageSender _email;
    private readonly EmailComposer _emailComposer;
    private readonly ILogger<MembersController> _logger;
    private readonly IStringLocalizer<SharedResources> _l;
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    public MembersController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IEmailMessageSender email,
        EmailComposer emailComposer,
        ILogger<MembersController> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _users = users;
        _email = email;
        _emailComposer = emailComposer;
        _logger = logger;
        _l = localizer;
    }

    private string TeacherId => _users.GetUserId(User)!;

    private async Task<Classroom?> LoadOwnedClassroomAsync(int classroomId)
        => await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId && c.OwnerId == TeacherId);

    [HttpGet]
    public async Task<IActionResult> Invite(int classroomId)
    {
        var classroom = await LoadOwnedClassroomAsync(classroomId);
        if (classroom == null) return NotFound();
        ViewBag.Classroom = classroom;
        return View(new InviteFormViewModel { ClassroomId = classroomId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteFormViewModel model)
    {
        var classroom = await LoadOwnedClassroomAsync(model.ClassroomId);
        if (classroom == null) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewBag.Classroom = classroom;
            return View(model);
        }

        var email = model.Email.Trim().ToLowerInvariant();

        // If the email already belongs to a member, short-circuit.
        var existingUser = await _users.FindByEmailAsync(email);
        if (existingUser != null)
        {
            var alreadyMember = await _db.ClassroomMembers
                .AnyAsync(m => m.ClassroomId == classroom.Id && m.StudentId == existingUser.Id);
            if (alreadyMember)
            {
                TempData["Error"] = _l["Toast.AlreadyMember"].Value;
                return RedirectToAction("Details", "Classrooms", new { id = classroom.Id });
            }
        }

        // De-dupe: if there's a pending non-expired invite for the same email, reuse its token.
        var pending = await _db.ClassroomInvites
            .FirstOrDefaultAsync(i =>
                i.ClassroomId == classroom.Id &&
                i.Email == email &&
                i.AcceptedAt == null &&
                i.ExpiresAt > DateTime.UtcNow);

        ClassroomInvite invite;
        if (pending != null)
        {
            invite = pending;
            _logger.LogInformation("Resending invite {InviteId} for {Email} to classroom {ClassroomId}",
                invite.Id, LogSanitizer.HashEmail(email), classroom.Id);
        }
        else
        {
            invite = new ClassroomInvite
            {
                ClassroomId = classroom.Id,
                Email = email,
                Token = InviteTokenGenerator.Generate(),
                ExpiresAt = DateTime.UtcNow.Add(InviteLifetime),
                CreatedAt = DateTime.UtcNow,
                CreatedById = TeacherId
            };
            _db.ClassroomInvites.Add(invite);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Created invite {InviteId} for {Email} to classroom {ClassroomId}",
                invite.Id, LogSanitizer.HashEmail(email), classroom.Id);
        }

        var acceptUrl = Url.Action("Accept", "Invites", new { area = "", token = invite.Token },
            Request.Scheme) ?? "";

        try
        {
            var invitedBy = DescribeTeacher(await _users.GetUserAsync(User));
            await _email.SendEmailAsync(email,
                await _emailComposer.ClassroomInviteAsync(invitedBy, classroom.Name, acceptUrl, invite.ExpiresAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed sending invite email to {Email}", LogSanitizer.HashEmail(email));
            TempData["Error"] = _l["Toast.InviteSavedNoEmail"].Value + " " + acceptUrl;
            return RedirectToAction("Details", "Classrooms", new { id = classroom.Id });
        }

        TempData["Success"] = _l["Toast.InviteSent", email].Value;
        return RedirectToAction("Details", "Classrooms", new { id = classroom.Id });
    }

    // Name and address together: a student may know only one of them.
    private string DescribeTeacher(ApplicationUser? teacher)
    {
        var address = teacher?.Email ?? User.Identity?.Name ?? "";
        var name = $"{teacher?.FirstName} {teacher?.LastName}".Trim();
        if (name.Length == 0) return address;
        return address.Length == 0 ? name : $"{name} ({address})";
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int classroomId, int memberId)
    {
        var classroom = await LoadOwnedClassroomAsync(classroomId);
        if (classroom == null) return NotFound();

        var member = await _db.ClassroomMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.ClassroomId == classroomId);
        if (member == null) return NotFound();

        _db.ClassroomMembers.Remove(member);
        await _db.SaveChangesAsync();

        TempData["Success"] = _l["Toast.MemberRemoved"].Value;
        return RedirectToAction("Details", "Classrooms", new { id = classroomId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelInvite(int classroomId, int inviteId)
    {
        var classroom = await LoadOwnedClassroomAsync(classroomId);
        if (classroom == null) return NotFound();

        var invite = await _db.ClassroomInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.ClassroomId == classroomId && i.AcceptedAt == null);
        if (invite == null) return NotFound();

        _db.ClassroomInvites.Remove(invite);
        await _db.SaveChangesAsync();

        TempData["Success"] = _l["Toast.InviteCancelled"].Value;
        return RedirectToAction("Details", "Classrooms", new { id = classroomId });
    }
}
