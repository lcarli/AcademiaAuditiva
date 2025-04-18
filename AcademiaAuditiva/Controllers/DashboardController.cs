using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcademiaAuditiva.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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
            return _context.Scores
                .Include(s => s.Exercise)
                .Where(s => s.UserId == userId)
                .GroupBy(s => s.ExerciseId)
                .Select(group => group.OrderByDescending(s => s.BestScore).FirstOrDefault())
                .ToList();
        }

        [HttpGet]
        public IActionResult GetUserProgress()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var scores = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                .ToList();

            var groupedByType = scores
                .Where(s => s.Exercise != null)
                .GroupBy(s => s.Exercise.Type)
                .Select(g => new
                {
                    Type = g.Key.ToString(),
                    Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                        : 0
                })
                .ToDictionary(x => x.Type, x => x.Accuracy);

            var groupedByCategory = scores
                .Where(s => s.Exercise != null)
                .GroupBy(s => s.Exercise.Category)
                .Select(g => new
                {
                    Category = g.Key.ToString(),
                    Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                        : 0
                })
                .ToDictionary(x => x.Category, x => x.Accuracy);

            return Json(new
            {
                radar = groupedByType,
                byCategory = groupedByCategory
            });
        }

        [HttpGet]
        public IActionResult GetUserTimeline()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var data = _context.Scores
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.Timestamp)
                .GroupBy(s => s.Timestamp.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Total = g.Count(),
                    Score = g.Sum(s => s.CorrectCount - s.ErrorCount)
                })
                .ToList();

            return Json(data);
        }

        [HttpGet]
        public IActionResult GetScoreHistory()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var history = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                .OrderByDescending(s => s.Timestamp)
                .Take(20)
                .Select(s => new
                {
                    Exercise = s.Exercise.Name,
                    Date = s.Timestamp.ToString("yyyy-MM-dd"),
                    Correct = s.CorrectCount,
                    Error = s.ErrorCount,
                    TimeSpent = s.TimeSpentSeconds / 60,
                    Score = s.CorrectCount - s.ErrorCount
                })
                .ToList();

            return Json(history);
        }

        [HttpGet]
        public IActionResult GetPerformanceByDifficulty()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var data = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                .GroupBy(s => s.Exercise.Difficulty)
                .Select(g => new
                {
                    Difficulty = g.Key.ToString(),
                    Accuracy = g.Sum(s => s.CorrectCount + s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100, 2)
                        : 0
                })
                .ToList();

            return Json(data);
        }

        [HttpGet]
        public IActionResult GetMostMissedItems()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var errors = _context.Scores
                .Where(s => s.UserId == userId && s.ErrorCount > 0)
                .Include(s => s.Exercise)
                .GroupBy(s => s.Exercise.Description)
                .Select(g => new
                {
                    Item = g.Key,
                    Errors = g.Sum(s => s.ErrorCount)
                })
                .OrderByDescending(x => x.Errors)
                .Take(10)
                .ToList();

            return Json(errors);
        }

        [HttpGet]
        public IActionResult GetRecommendations()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var scores = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                .OrderByDescending(s => s.Timestamp)
                .ToList();

            var recs = new List<string>();

            if (scores.Count >= 5)
            {
                var last = scores.Take(5);
                var acc = last.Sum(s => s.CorrectCount) + last.Sum(s => s.ErrorCount);

                if (acc > 0)
                {
                    var accuracy = (double)last.Sum(s => s.CorrectCount) / acc;
                    if (accuracy < 0.6)
                        recs.Add("Você tem cometido muitos erros recentemente. Refaça exercícios anteriores.");
                    else if (accuracy > 0.85)
                        recs.Add("Parabéns! Sua média de acertos está excelente! Que tal subir a dificuldade?");
                }
            }

            var mostMissed = _context.Scores
                .Where(s => s.UserId == userId && s.ErrorCount > 0)
                .Include(s => s.Exercise)
                .ToList()
                .GroupBy(s => s.Exercise.Description)
                .OrderByDescending(g => g.Sum(s => s.ErrorCount))
                .FirstOrDefault();

            if (mostMissed != null)
                recs.Add($"Considere revisar o exercício: {mostMissed.Key}");

            return Json(recs);
        }
    }
}
