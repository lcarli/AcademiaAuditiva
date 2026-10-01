using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.LearningPath;
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

    public async Task<IActionResult> Index()
    {
        var progress = await _learningPath.GetProgressAsync(_users.GetUserId(User)!, HttpContext.RequestAborted);
        return View(progress);
    }
}
