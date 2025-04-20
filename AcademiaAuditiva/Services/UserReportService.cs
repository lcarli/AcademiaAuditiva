using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services;
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

    public List<DateTime> GetPracticeDays(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.Timestamp.Date)
            .Distinct()
            .ToList();
    }

    public IEnumerable<object> GetMostPracticedExercises(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new
            {
                Exercise = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    public IEnumerable<object> GetHourlyActivity(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.Hour)
            .Select(g => new
            {
                Hour = g.Key,
                Count = g.Count()
            })
            .OrderBy(x => x.Hour)
            .ToList();
    }

    public IEnumerable<object> GetAccuracyByExercise(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new
            {
                Exercise = g.Key,
                Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                    ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100, 2)
                    : 0
            })
            .ToList();
    }

    public int GetConsecutivePracticeStreak(string userId)
    {
        var practiceDays = _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.Timestamp.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        int streak = 0;
        DateTime? lastDate = null;

        foreach (var date in practiceDays)
        {
            if (lastDate == null || (lastDate.Value - date).Days == 1)
            {
                streak++;
            }
            else if (lastDate.Value != date)
            {
                break;
            }
            lastDate = date;
        }

        return streak;
    }

    public double GetAverageResponseTimeByExercise(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new
            {
                Exercise = g.Key,
                AverageResponseTime = g.Average(s => s.TimeSpentSeconds)
            })
            .ToList()
            .Average(e => e.AverageResponseTime);
    }

    public IDictionary<string, double> GetAccuracyTrendByDay(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => new
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                    ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                    : 0
            })
            .ToDictionary(x => x.Date, x => x.Accuracy);
    }

    public IEnumerable<object> GetMostErrorProneExercises(string userId)
    {
        var errorProneExercises = _context.Scores
            .Where(s => s.UserId == userId && s.ErrorCount > 0 && s.Exercise != null)
            .GroupBy(s => s.Exercise.Description)
            .Select(g => new
            {
                Exercise = g.Key,
                TotalErrors = g.Sum(s => s.ErrorCount)
            })
            .OrderByDescending(x => x.TotalErrors)
            .Take(10)
            .ToList();

        return errorProneExercises;
    }

    public double GetExerciseVarietyRatio(string userId)
    {
        var totalExercises = _context.Exercises.Count();
        var uniqueExercises = _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.ExerciseId)
            .Distinct()
            .Count();

        return totalExercises > 0 ? (double)uniqueExercises / totalExercises : 0;
    }

    public IDictionary<string, int> GetAttemptDistributionByDifficulty(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.DifficultyLevel.Name)
            .Select(g => new
            {
                Difficulty = g.Key,
                Count = g.Count()
            })
            .ToDictionary(x => x.Difficulty, x => x.Count);
    }

    public double GetOverallAccuracy(string userId)
    {
        var scores = _context.Scores
            .Where(s => s.UserId == userId)
            .ToList();

        var totalCorrect = scores.Sum(s => s.CorrectCount);
        var totalAttempts = scores.Sum(s => s.CorrectCount + s.ErrorCount);

        return totalAttempts > 0 ? Math.Round((double)totalCorrect / totalAttempts * 100, 2) : 0;
    }

    public IDictionary<string, double> GetAccuracyByCategory(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.ExerciseCategory.Name)
            .Select(g => new
            {
                Category = g.Key,
                Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                    ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                    : 0
            })
            .ToDictionary(x => x.Category, x => x.Accuracy);
    }

    public IDictionary<string, double> GetAccuracyByDifficulty(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.DifficultyLevel.Name)
            .Select(g => new
            {
                Difficulty = g.Key,
                Accuracy = g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount) > 0
                    ? Math.Round((double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount) + g.Sum(s => s.ErrorCount)) * 100, 2)
                    : 0
            })
            .ToDictionary(x => x.Difficulty, x => x.Accuracy);
    }

    public IDictionary<string, double> GetImprovementBetweenFirstAndLastAttempt(string userId)
    {
        var result = new Dictionary<string, double>();
        var scoresByExercise = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .GroupBy(s => s.Exercise.Name);

        foreach (var group in scoresByExercise)
        {
            var first = group.OrderBy(s => s.Timestamp).FirstOrDefault();
            var last = group.OrderBy(s => s.Timestamp).LastOrDefault();
            if (first != null && last != null)
            {
                double firstAccuracy = (first.CorrectCount + first.ErrorCount) > 0
                    ? (double)first.CorrectCount / (first.CorrectCount + first.ErrorCount) * 100
                    : 0;
                double lastAccuracy = (last.CorrectCount + last.ErrorCount) > 0
                    ? (double)last.CorrectCount / (last.CorrectCount + last.ErrorCount) * 100
                    : 0;
                result[group.Key] = Math.Round(lastAccuracy - firstAccuracy, 2);
            }
        }
        return result;
    }

    public string GetExerciseWithHighestErrorRate(string userId)
    {
        var exerciseErrorRates = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new
            {
                Exercise = g.Key,
                ErrorRate = g.Sum(s => s.CorrectCount + s.ErrorCount) > 0
                    ? (double)g.Sum(s => s.ErrorCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100
                    : 0
            });

        var max = exerciseErrorRates.OrderByDescending(x => x.ErrorRate).FirstOrDefault();
        return max?.Exercise;
    }

    public string GetExerciseWithHighestAccuracy(string userId)
    {
        var exerciseAccuracies = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new
            {
                Exercise = g.Key,
                Accuracy = g.Sum(s => s.CorrectCount + s.ErrorCount) > 0
                    ? (double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100
                    : 0
            });

        var max = exerciseAccuracies.OrderByDescending(x => x.Accuracy).FirstOrDefault();
        return max?.Exercise;
    }

    public List<string> GetPerfectScoreExercises(string userId)
    {
        var perfectExercises = _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise)
            .Where(g => g.All(s => s.ErrorCount == 0))
            .Select(g => g.Key.Name)
            .Distinct()
            .ToList();

        return perfectExercises;
    }

    public List<string> GetNeverAttemptedExercises(string userId)
    {
        var attemptedExerciseIds = _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.ExerciseId)
            .Distinct();

        var neverAttempted = _context.Exercises
            .Where(e => !attemptedExerciseIds.Contains(e.ExerciseId))
            .Select(e => e.Name)
            .ToList();

        return neverAttempted;
    }
    public IDictionary<string, int> GetMostActiveWeekdays(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.DayOfWeek.ToString())
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Day, x => x.Count);
    }

    public int GetMostCommonHour(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.Hour)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    public double GetAverageTimePerAttempt(string userId)
    {
        var attempts = _context.Scores.Where(s => s.UserId == userId).ToList();
        return attempts.Any() ? attempts.Average(s => s.TimeSpentSeconds) : 0;
    }

    public double GetTotalTimePracticed(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .Sum(s => s.TimeSpentSeconds);
    }

    public IDictionary<string, double> GetAverageTimePerExercise(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new { Exercise = g.Key, AverageTime = g.Average(s => s.TimeSpentSeconds) })
            .ToDictionary(x => x.Exercise, x => x.AverageTime);
    }

    public IDictionary<string, int> GetTotalTimePerExercise(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new { Exercise = g.Key, TotalTime = g.Sum(s => s.TimeSpentSeconds) })
            .ToDictionary(x => x.Exercise, x => x.TotalTime);
    }

    public double GetAverageAttemptsPerDay(string userId)
    {
        var attemptsByDay = _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => g.Count())
            .ToList();
        return attemptsByDay.Any() ? attemptsByDay.Average() : 0;
    }

    public int GetTotalSessions(string userId)
    {
        return _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.Timestamp.Date)
            .Distinct()
            .Count();
    }
}
