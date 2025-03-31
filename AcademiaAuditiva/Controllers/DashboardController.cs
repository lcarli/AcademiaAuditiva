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

            // Agrupar por tipo técnico (ExerciseType)
            var groupedByType = scores
                .Where(s => s.Exercise != null)
                .GroupBy(s => s.Exercise.Type)
                .Select(g => new
                {
                    Type = g.Key.ToString(),
                    Total = g.Count(),
                    Correct = g.Sum(s => s.CorrectCount),
                    Error = g.Sum(s => s.ErrorCount),
                    Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                        : 0
                })
                .ToDictionary(x => x.Type, x => x.Accuracy);

            // Agrupar por categoria (pedagógica), se quiser incluir futuramente
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
    }
}
