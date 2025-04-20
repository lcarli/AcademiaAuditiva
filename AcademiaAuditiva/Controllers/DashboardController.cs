using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Security.Claims;

namespace AcademiaAuditiva.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IStringLocalizer<SharedResources> _localizer;
        private readonly UserReportService _userReportService;

        public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResources> localizer, UserReportService userReportService)
        {
            _context = context;
            _userManager = userManager;
            _localizer = localizer;
            _userReportService = userReportService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var user = await _userManager.FindByIdAsync(userId);

            ViewBag.FirstName = user.FirstName;

            var userScores = _context.Scores
                .Where(s => s.UserId == userId)
                .ToList();

            var totalExercises = userScores.Count;

            var bestScore = userScores
                .Select(s => s.CorrectCount - s.ErrorCount)
                .DefaultIfEmpty(0)
                .Max();

            var totalTimeMinutes = userScores.Sum(s => s.TimeSpentSeconds) / 60;

            ViewBag.TotalExercises = totalExercises;
            ViewBag.BestScore = bestScore;
            ViewBag.TotalTime = totalTimeMinutes;

            return View();
        }

        [AllowAnonymous]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
            );

            return LocalRedirect(returnUrl);
        }

        public List<Score> GetBestScoresForUser(string userId)
        {
            return _userReportService.GetBestScoresForUser(userId);
        }

        [HttpGet]
        public IActionResult GetUserProgress()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = _userReportService.GetUserProgress(userId);
            return Json(result);
        }

        [HttpGet]
        public IActionResult GetUserTimeline()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var data = _userReportService.GetUserTimeline(userId);
            return Json(data);
        }

        [HttpGet]
        public IActionResult GetScoreHistory()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var history = _userReportService.GetScoreHistory(userId);
            return Json(history);
        }

        [HttpGet]
        public IActionResult GetPerformanceByDifficulty()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var performance = _userReportService.GetPerformanceByDifficulty(userId);
            return Json(performance);
        }

        [HttpGet]
        public IActionResult GetMostMissedItems()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var errors = _userReportService.GetMostMissedItems(userId);
            return Json(errors);
        }

        [HttpGet]
        public IActionResult GetRecommendations()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var recs = _userReportService.GetRecommendations(userId);
            return Json(recs);
        }

        [HttpGet]
        public IActionResult GetExerciseTranslations()
        {
            var exerciseNames = _context.Exercises
                .Select(e => e.Name)
                .Distinct()
                .ToList();

            var translations = exerciseNames.ToDictionary(
                name => name,
                name => _localizer[$"{name}"].Value
            );

            return Json(translations);
        }
    }
}