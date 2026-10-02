using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.Controllers;

/// <summary>Remembers the guided tours each user has closed (wwwroot/js/core/tutorial.js).</summary>
[Authorize]
public class TutorialController : Controller
{
    private readonly ITutorialService _tutorials;
    private readonly UserManager<ApplicationUser> _users;

    public TutorialController(ITutorialService tutorials, UserManager<ApplicationUser> users)
    {
        _tutorials = tutorials;
        _users = users;
    }

    /// <param name="finished">True when the user reached the last step, false when they skipped the tour.</param>
    [HttpPost]
    public async Task<IActionResult> Seen(string? key, bool finished)
    {
        if (!TutorialCatalog.TryGet(key, out var tutorial)) return BadRequest();

        await _tutorials.MarkSeenAsync(_users.GetUserId(User)!, tutorial.Key, finished, HttpContext.RequestAborted);
        return NoContent();
    }
}
