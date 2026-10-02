using AcademiaAuditiva.Areas.Admin.Models;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Areas.Admin.Controllers;

public class UsersController : AdminAreaController
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly PersonalDataService _personalData;
    private readonly ILogger<UsersController> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public UsersController(
        UserManager<ApplicationUser> users,
        PersonalDataService personalData,
        ILogger<UsersController> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _users = users;
        _personalData = personalData;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index(string? q = null, string? role = null, int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var query = _users.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = q.Trim().ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(like)) ||
                (u.Email != null && u.Email.ToLower().Contains(like)));
        }

        var users = await query.OrderBy(u => u.UserName).Take(take).ToListAsync();
        var rows = new List<UserListRow>(users.Count);
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            var row = new UserListRow
            {
                Id = u.Id,
                UserName = u.UserName ?? "",
                Email = u.Email ?? "",
                IsAdmin = roles.Contains(RoleNames.Admin),
                IsTeacher = roles.Contains(RoleNames.Teacher),
                IsStudent = roles.Contains(RoleNames.Student),
                IsLockedOut = await _users.IsLockedOutAsync(u),
                EmailConfirmed = u.EmailConfirmed
            };
            if (!string.IsNullOrEmpty(role))
            {
                if (role == RoleNames.Admin && !row.IsAdmin) continue;
                if (role == RoleNames.Teacher && !row.IsTeacher) continue;
                if (role == RoleNames.Student && !row.IsStudent) continue;
            }
            rows.Add(row);
        }

        return View(new UserListViewModel
        {
            Rows = rows,
            Query = q,
            Role = role,
            Total = rows.Count
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteTeacher(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (!await _users.IsInRoleAsync(u, RoleNames.Teacher))
        {
            await _users.AddToRoleAsync(u, RoleNames.Teacher);
            _logger.LogInformation("Admin {Admin} promoted user {UserId} to Teacher", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        }
        TempData["Success"] = _localizer["Admin.Users.PromotedTeacher", u.UserName ?? u.Email ?? id].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteTeacher(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (await _users.IsInRoleAsync(u, RoleNames.Teacher))
        {
            await _users.RemoveFromRoleAsync(u, RoleNames.Teacher);
            _logger.LogInformation("Admin {Admin} demoted teacher {UserId} to student", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        }
        TempData["Success"] = _localizer["Admin.Users.DemotedTeacher", u.UserName ?? u.Email ?? id].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteAdmin(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (!await _users.IsInRoleAsync(u, RoleNames.Admin))
        {
            await _users.AddToRoleAsync(u, RoleNames.Admin);
            _logger.LogWarning("Admin {Admin} promoted user {UserId} to ADMIN", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        }
        TempData["Success"] = _localizer["Admin.Users.PromotedAdmin", u.UserName ?? u.Email ?? id].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteAdmin(string id)
    {
        var currentId = _users.GetUserId(User);
        if (id == currentId)
        {
            TempData["Error"] = _localizer["Admin.Users.CannotDemoteSelf"].Value;
            return RedirectToAction(nameof(Index));
        }
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();

        // Refuse to demote the last admin to avoid lockout.
        var admins = await _users.GetUsersInRoleAsync(RoleNames.Admin);
        if (admins.Count <= 1)
        {
            TempData["Error"] = _localizer["Admin.Users.CannotDemoteLastAdmin"].Value;
            return RedirectToAction(nameof(Index));
        }

        if (await _users.IsInRoleAsync(u, RoleNames.Admin))
        {
            await _users.RemoveFromRoleAsync(u, RoleNames.Admin);
            _logger.LogWarning("Admin {Admin} demoted admin {UserId}", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        }
        TempData["Success"] = _localizer["Admin.Users.DemotedAdmin", u.UserName ?? u.Email ?? id].Value;
        return RedirectToAction(nameof(Index));
    }

    // Admins (you included) can't be locked or deleted: remove the Admin role
    // first, which DemoteAdmin refuses for yourself and for the last admin.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (await RefusalAsync(u, "Admin.Users.CannotLockSelf", "Admin.Users.CannotLockAdmin") is { } refusal)
        {
            TempData["Error"] = refusal;
            return RedirectToAction(nameof(Index));
        }

        // Identity ignores LockoutEnd unless lockout is enabled for the account.
        // Open sessions end at their next cookie check (LockoutAwareSignInManager);
        // the new security stamp also voids links already sent, like password resets.
        var result = IdentityResult.Success;
        if (!await _users.GetLockoutEnabledAsync(u)) result = await _users.SetLockoutEnabledAsync(u, true);
        if (result.Succeeded) result = await _users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue);
        if (result.Succeeded) result = await _users.UpdateSecurityStampAsync(u);
        if (!result.Succeeded)
        {
            TempData["Error"] = _localizer["Admin.Users.UpdateFailed", DisplayName(u)].Value;
            return RedirectToAction(nameof(Index));
        }

        _logger.LogWarning("Admin {Admin} locked user {UserId}", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        TempData["Success"] = _localizer["Admin.Users.LockedUser", DisplayName(u)].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();

        var result = IdentityResult.Success;
        if (await _users.IsLockedOutAsync(u)) result = await _users.SetLockoutEndDateAsync(u, null);
        if (result.Succeeded) result = await _users.ResetAccessFailedCountAsync(u);
        if (!result.Succeeded)
        {
            TempData["Error"] = _localizer["Admin.Users.UpdateFailed", DisplayName(u)].Value;
            return RedirectToAction(nameof(Index));
        }

        _logger.LogWarning("Admin {Admin} unlocked user {UserId}", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        TempData["Success"] = _localizer["Admin.Users.UnlockedUser", DisplayName(u)].Value;
        return RedirectToAction(nameof(Index));
    }

    // Asks first, showing the classes and routines that go with the account.
    [HttpGet]
    public async Task<IActionResult> Delete(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (await RefusalAsync(u, "Admin.Users.CannotDeleteSelf", "Admin.Users.CannotDeleteAdmin") is { } refusal)
        {
            TempData["Error"] = refusal;
            return RedirectToAction(nameof(Index));
        }

        var roles = await _users.GetRolesAsync(u);
        var (classrooms, routines) = await _personalData.CountTeachingDataAsync(u.Id, HttpContext.RequestAborted);
        return View(new DeleteUserViewModel
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            Email = u.Email ?? "",
            IsTeacher = roles.Contains(RoleNames.Teacher),
            IsStudent = roles.Contains(RoleNames.Student),
            ClassroomCount = classrooms,
            RoutineCount = routines
        });
    }

    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (await RefusalAsync(u, "Admin.Users.CannotDeleteSelf", "Admin.Users.CannotDeleteAdmin") is { } refusal)
        {
            TempData["Error"] = refusal;
            return RedirectToAction(nameof(Index));
        }

        var name = DisplayName(u);
        var result = await _personalData.DeleteAccountAsync(u, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = _localizer["Admin.Users.DeleteFailed", name].Value;
            return RedirectToAction(nameof(Index));
        }

        _logger.LogWarning("Admin {Admin} deleted user {UserId}", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        TempData["Success"] = _localizer["Admin.Users.DeletedUser", name].Value;
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> RefusalAsync(ApplicationUser u, string selfKey, string adminKey)
    {
        if (u.Id == _users.GetUserId(User)) return _localizer[selfKey].Value;
        if (await _users.IsInRoleAsync(u, RoleNames.Admin)) return _localizer[adminKey].Value;
        return null;
    }

    private static string DisplayName(ApplicationUser u) => u.UserName ?? u.Email ?? u.Id;
}
