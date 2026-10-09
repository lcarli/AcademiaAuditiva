using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.LearningPath;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.Controllers;

[Authorize]
public class LearningPathController : Controller
{
    private readonly ILearningPathService _learningPath;
    private readonly UserManager<ApplicationUser> _users;

    public LearningPathController(ILearningPathService learningPath, UserManager<ApplicationUser> users)
    {
        _learningPath = learningPath;
        _users = users;
    }

    public async Task<IActionResult> Index(string? track = null)
    {
        var current = string.IsNullOrWhiteSpace(track) ? TrainingTracks.Find(TrainingTracks.Music) : TrainingTracks.Find(track);
        if (current is null || LearningPathCatalog.UnitsFor(current.Key).Count == 0)
            return NotFound();

        var progress = await _learningPath.GetProgressAsync(
            _users.GetUserId(User)!, current.Key, HttpContext.RequestAborted);
        ViewBag.Track = current.Key;
        ViewBag.Tracks = TrainingTracks.All
            .Where(t => LearningPathCatalog.UnitsFor(t.Key).Count > 0)
            .ToList();
        return View(progress);
    }
}
