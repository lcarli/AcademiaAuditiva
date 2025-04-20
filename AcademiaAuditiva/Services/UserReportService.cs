using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services
{
    public class UserReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserReportService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public List<Score> GetBestScoresForUser(string userId)
        {
            var scores = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                    .ThenInclude(e => e.ExerciseType)
                .Include(s => s.Exercise)
                    .ThenInclude(e => e.ExerciseCategory)
                .OrderByDescending(s => s.CorrectCount)
                .ToList();

            return scores;
        }

        public object GetUserProgress(string userId)
        {
            var scores = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                    .ThenInclude(e => e.ExerciseType)
                .Include(s => s.Exercise)
                    .ThenInclude(e => e.ExerciseCategory)
                .ToList();

            var groupedByType = scores
                .Where(s => s.Exercise != null && s.Exercise.ExerciseType != null)
                .GroupBy(s => s.Exercise.ExerciseType)
                .Select(g => new
                {
                    Type = g.Key.Name,
                    Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                        : 0
                })
                .GroupBy(x => x.Type)
                .Select(g => g.First())
                .ToDictionary(x => x.Type, x => x.Accuracy);

            var groupedByCategory = scores
                .Where(s => s.Exercise != null && s.Exercise.ExerciseType != null)
                .GroupBy(s => s.Exercise.ExerciseCategory)
                .Select(g => new
                {
                    Category = g.Key.Name,
                    Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                        : 0
                })
                .GroupBy(x => x.Category)
                .Select(g => g.First())
                .ToDictionary(x => x.Category, x => x.Accuracy);

            return new
            {
                radar = groupedByType,
                byCategory = groupedByCategory
            };
        }

        public object GetUserTimeline(string userId)
        {
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


            return data;
        }

        public IEnumerable<object> GetScoreHistory(string userId)
        {
            var scores = _context.Scores
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

            return scores;
        }

        public IEnumerable<object> GetMostMissedItems(string userId)
        {
            var errors = _context.Scores
                .Where(s => s.UserId == userId && s.ErrorCount > 0)
                .Include(s => s.Exercise)
                .GroupBy(s => s.Exercise.Description)
                .Select(g => new
                {
                    Exercise = g.Key,
                    Errors = g.Sum(s => s.ErrorCount)
                })
                .OrderByDescending(x => x.Errors)
                .Take(10)
                .ToList();

            return errors;
        }

        public List<string> GetRecommendations(string userId)
        {
            var recentScores = _context.Scores
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.DateCreated)
                .Take(5)
                .ToList();

            var recommendations = new List<string>();

            var mostMissedExerciseId = recentScores
                .GroupBy(s => s.ExerciseId)
                .OrderByDescending(g => g.Sum(s => s.ErrorCount))
                .Select(g => g.Key)
                .FirstOrDefault();

            if (mostMissedExerciseId != null)
            {
                var exerciseName = _context.Exercises
                    .Where(e => e.ExerciseId == mostMissedExerciseId)
                    .Select(e => e.Name)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(exerciseName))
                {
                    recommendations.Add($"Revisar o exercício: {exerciseName}");
                }
            }

            if (recentScores.Count < 5)
            {
                recommendations.Add("Continue praticando mais exercícios para melhorar seu desempenho.");
            }

            return recommendations;
        }


        public object GetPerformanceByDifficulty(string userId)
        {
            var data = _context.Scores
                .Where(s => s.UserId == userId)
                .Include(s => s.Exercise)
                .ThenInclude(e => e.DifficultyLevel)
                .GroupBy(s => s.Exercise.DifficultyLevel)
                .Select(g => new
                {
                    Difficulty = g.Key.Name,
                    Accuracy = g.Sum(s => s.CorrectCount + s.ErrorCount) > 0
                        ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100, 2)
                        : 0
                })
                .ToList();

            return data;
        }
    }
}
