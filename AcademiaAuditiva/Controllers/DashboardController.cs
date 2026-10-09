using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

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
        private readonly IDailyChallengeService _dailyChallenge;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IStringLocalizer<SharedResources> localizer,
            UserReportService userReportService,
            IGamificationService gamification,
            ILearningPathService learningPath,
            IDailyChallengeService dailyChallenge,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _userManager = userManager;
            _localizer = localizer;
            _userReportService = userReportService;
            _gamification = gamification;
            _learningPath = learningPath;
            _dailyChallenge = dailyChallenge;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string? track = null)
        {
            var current = string.IsNullOrWhiteSpace(track) ? TrainingTracks.Find(TrainingTracks.Music) : TrainingTracks.Find(track);
            if (current is null || LearningPathCatalog.UnitsFor(current.Key).Count == 0)
                return NotFound();
            ViewBag.Track = current.Key;
            ViewBag.Tracks = TrainingTracks.All.Where(t => LearningPathCatalog.UnitsFor(t.Key).Count > 0).ToList();
            var userId = _userManager.GetUserId(User)!;
            var user = await _userManager.FindByIdAsync(userId);

            ViewBag.FirstName = user?.FirstName;

            var summary = await _userReportService.GetSummaryAsync(userId, HttpContext.RequestAborted, current.Key);
            ViewBag.TotalAnswers = summary.Answers;
            ViewBag.BestScore = summary.BestScore;
            ViewBag.TotalTime = summary.TotalSeconds / 60;

            // The history and most-missed lists load later and name exercises by identifier
            // (GuessNote); the page shows them through this map of the whole catalogue.
            var exerciseNames = await _context.Exercises
                .Select(e => e.Name)
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted);
            ViewBag.ExerciseNames = exerciseNames.ToDictionary(name => name, name => _localizer[name].Value);

            var timeZone = UserTimeZone.FromRequest(Request);

            // The progress row is optional: the rest of the dashboard still renders without it.
            GamificationProfile? profile = null;
            try
            {
                profile = await _gamification.GetProfileAsync(userId, timeZone, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not load the gamification profile for the dashboard.");
            }

            // So is the learning path card.
            try
            {
                ViewBag.LearningPath = await _learningPath.GetProgressAsync(userId, current.Key, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not load the learning path for the dashboard.");
            }

            // And the daily challenge card.
            try
            {
                ViewBag.DailyChallenge = await _dailyChallenge.GetTodayAsync(userId, timeZone, current.Key, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not load the daily challenge for the dashboard.");
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
        public async Task<IActionResult> SetLanguage(string culture, string returnUrl, [FromServices] IOptions<RequestLocalizationOptions> localization)
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

                // On the account too, for the e-mails sent to the user while they are away.
                if (User.Identity?.IsAuthenticated == true
                    && await _userManager.GetUserAsync(User) is { } user
                    && user.Language != supported.Name)
                {
                    user.Language = supported.Name;
                    var saved = await _userManager.UpdateAsync(user);
                    if (!saved.Succeeded)
                        _logger.LogWarning("Could not save the language of user {UserId}: {Errors}",
                            user.Id, string.Join(", ", saved.Errors.Select(e => e.Code)));
                }
            }

            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
        }

        // Accuracy per exercise type (radar) and per category.
        [HttpGet]
        public async Task<IActionResult> GetUserProgress(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetSkillProfileAsync(_userManager.GetUserId(User)!, ct, current.Key))
                : NotFound();

        // Answers and accuracy per day, by the student's calendar.
        [HttpGet]
        public async Task<IActionResult> GetUserTimeline(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetTimelineAsync(_userManager.GetUserId(User)!, UserTimeZone.FromRequest(Request), ct, current.Key))
                : NotFound();

        // The latest practice sessions.
        [HttpGet]
        public async Task<IActionResult> GetScoreHistory(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetRecentSessionsAsync(_userManager.GetUserId(User)!, ct, current.Key))
                : NotFound();

        [HttpGet]
        public async Task<IActionResult> GetPerformanceByDifficulty(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetAccuracyByDifficultyAsync(_userManager.GetUserId(User)!, ct, current.Key))
                : NotFound();

        // The exercises most often answered wrong lately.
        [HttpGet]
        public async Task<IActionResult> GetMostMissedItems(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetStrugglesAsync(_userManager.GetUserId(User)!, ct, current.Key))
                : NotFound();

        [HttpGet]
        public async Task<IActionResult> GetRecommendations(CancellationToken ct, string track = TrainingTracks.Music) =>
            TrainingTracks.Find(track) is { } current
                ? Json(await _userReportService.GetRecommendationsAsync(_userManager.GetUserId(User)!, ct, current.Key))
                : NotFound();
    }
}