using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
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
        private readonly IGamificationService _gamification;
        private readonly ILearningPathService _learningPath;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IStringLocalizer<SharedResources> localizer,
            UserReportService userReportService,
            IGamificationService gamification,
            ILearningPathService learningPath,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _userManager = userManager;
            _localizer = localizer;
            _userReportService = userReportService;
            _gamification = gamification;
            _learningPath = learningPath;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var user = await _userManager.FindByIdAsync(userId);

            ViewBag.FirstName = user?.FirstName;

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

            // The progress row is optional: the rest of the dashboard still renders without it.
            GamificationProfile? profile = null;
            try
            {
                profile = await _gamification.GetProfileAsync(userId, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not load the gamification profile for the dashboard.");
            }

            // So is the learning path card.
            try
            {
                ViewBag.LearningPath = await _learningPath.GetProgressAsync(userId, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not load the learning path for the dashboard.");
            }

            return View(profile);
        }

        public async Task<IActionResult> Achievements()
        {
            var userId = _userManager.GetUserId(User)!;
            var profile = await _gamification.GetProfileAsync(userId, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);

            // The model keeps IsNew so this visit can still highlight what was just unlocked.
            await _gamification.MarkBadgesSeenAsync(userId, HttpContext.RequestAborted);
            return View(profile);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetLanguage(string culture, string returnUrl, [FromServices] IOptions<RequestLocalizationOptions> localization)
        {
            var supported = localization.Value.SupportedUICultures?
                .FirstOrDefault(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase));

            // Unknown cultures are ignored rather than stored: RequestCulture would throw
            // on an invalid name, and the middleware would discard an unsupported one anyway.
            if (supported != null)
            {
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supported)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax
                    });
            }

            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
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