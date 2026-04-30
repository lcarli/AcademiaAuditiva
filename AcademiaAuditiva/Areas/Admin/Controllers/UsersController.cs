using AcademiaAuditiva.Areas.Admin.Models;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Admin.Controllers;

public class UsersController : AdminAreaController
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserManager<ApplicationUser> users, ILogger<UsersController> logger)
    {
        _users = users;
        _logger = logger;
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
                LockoutEndUtc = u.LockoutEnd?.UtcDateTime,
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
        TempData["Success"] = $"{u.UserName} is now a Teacher.";
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
        TempData["Success"] = $"{u.UserName} is no longer a Teacher.";
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
        TempData["Success"] = $"{u.UserName} is now an Admin.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteAdmin(string id)
    {
        var currentId = _users.GetUserId(User);
        if (id == currentId)
        {
            TempData["Error"] = "You cannot demote yourself.";
            return RedirectToAction(nameof(Index));
        }
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();

        // Refuse to demote the last admin to avoid lockout.
        var admins = await _users.GetUsersInRoleAsync(RoleNames.Admin);
        if (admins.Count <= 1)
        {
            TempData["Error"] = "Cannot demote the last remaining Admin.";
            return RedirectToAction(nameof(Index));
        }

        if (await _users.IsInRoleAsync(u, RoleNames.Admin))
        {
            await _users.RemoveFromRoleAsync(u, RoleNames.Admin);
            _logger.LogWarning("Admin {Admin} demoted admin {UserId}", LogSanitizer.Sanitize(User.Identity?.Name), LogSanitizer.Sanitize(id));
        }
        TempData["Success"] = $"{u.UserName} is no longer an Admin.";
        return RedirectToAction(nameof(Index));
    }
}
