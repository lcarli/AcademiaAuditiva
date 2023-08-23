using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authorization;
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
            return View();
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

        public IActionResult GetUserProgress()
        {
            string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var bestScores = GetBestScoresForUser(currentUserId);

            var result = bestScores.Select(score => new
            {
				ExerciseName = score.Exercise.Name,
				ExerciseId = score.ExerciseId,
                CorrectCount = score.CorrectCount,
                ErrorCount = score.ErrorCount,
                BestScore = score.BestScore,
                Date = score.DateCreated
            });

            return Json(result);
        }


    }
}
