using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services;
public class UserReportService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AnalyticsService _analyticsService;

    public UserReportService(ApplicationDbContext context, UserManager<ApplicationUser> userManager, AnalyticsService analyticsService)
    {
        _context = context;
        _userManager = userManager;
        _analyticsService = analyticsService;
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

    public async Task<IEnumerable<object>> GetMostFrequentWrongAnswers(string userId)
    {
        var logs = await _analyticsService.GetAttemptsAsync(userId);

        return logs
            .Where(a => a.Attempt != null && a.Attempt.UserAnswer != null && !a.Attempt.IsCorrect)
            .GroupBy(a => a.Attempt.UserAnswer?.ToString())
            .Select(g => new
            {
                WrongAnswer = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();
    }

    public async Task<IEnumerable<object>> GetMostMissedExpectedAnswers(string userId)
    {
        var logs = await _analyticsService.GetAttemptsAsync(userId);

        return logs
            .Where(a => a.Attempt != null && a.Attempt.ExpectedAnswer != null && !a.Attempt.IsCorrect)
            .GroupBy(a => a.Attempt.ExpectedAnswer?.ToString())
            .Select(g => new
            {
                ExpectedAnswer = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();
    }

    public IEnumerable<object> GetRecurringErrorsPerExercise(string userId)
    {
        var scores = _context.Scores
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp)
            .ToList();

        var result = new List<object>();
        var grouped = scores.GroupBy(s => s.Exercise.Name);

        foreach (var group in grouped)
        {
            int maxConsecutiveErrors = 0;
            int currentStreak = 0;
            foreach (var score in group)
            {
                if (score.ErrorCount > 0)
                    currentStreak++;
                else
                    currentStreak = 0;

                if (currentStreak > maxConsecutiveErrors)
                    maxConsecutiveErrors = currentStreak;
            }

            if (maxConsecutiveErrors > 1)
            {
                result.Add(new { Exercise = group.Key, ConsecutiveErrors = maxConsecutiveErrors });
            }
        }

        return result;
    }

    public double GetAverageAttemptsUntilCorrect(string userId)
    {
        var scores = _context.Scores
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp)
            .ToList();

        var groups = scores.GroupBy(s => s.Exercise.Name);
        var attemptsList = new List<int>();

        foreach (var group in groups)
        {
            int attempts = 0;
            foreach (var score in group)
            {
                attempts++;
                if (score.CorrectCount > 0)
                {
                    attemptsList.Add(attempts);
                    break;
                }
            }
        }

        return attemptsList.Any() ? attemptsList.Average() : 0;
    }

    public List<int> GetPracticeGaps(string userId)
    {
        var practiceDays = _context.Scores
            .Where(s => s.UserId == userId)
            .Select(s => s.Timestamp.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var gaps = new List<int>();
        for (int i = 1; i < practiceDays.Count; i++)
        {
            gaps.Add((practiceDays[i] - practiceDays[i - 1]).Days);
        }
        return gaps;
    }

    public IDictionary<string, double> GetEngagementTrend(string userId)
    {
        var trend = _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => new {
                s.Timestamp.Year,
                Week = System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                    s.Timestamp, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)
            })
            .Select(g => new {
                WeekLabel = $"{g.Key.Year}-W{g.Key.Week}",
                Attempts = g.Count()
            })
            .ToDictionary(x => x.WeekLabel, x => (double)x.Attempts);
        return trend;
    }

    public List<string> GetRepetitionPatterns(string userId)
    {
        var scores = _context.Scores
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp)
            .ToList();
 
        var patterns = new List<string>();
        if (!scores.Any())
            return patterns;
 
        string currentExercise = scores[0].Exercise?.Name;
        int count = 1;
 
        for (int i = 1; i < scores.Count; i++)
        {
            var exerciseName = scores[i].Exercise?.Name;
            if (exerciseName == currentExercise)
            {
                count++;
            }
            else
            {
                if (count > 1)
                {
                    patterns.Add($"{currentExercise} x {count}");
                }
                currentExercise = exerciseName;
                count = 1;
            }
        }
        if (count > 1)
        {
            patterns.Add($"{currentExercise} x {count}");
        }
        return patterns;
    }
    
    public IEnumerable<object> GetDifficultyChangeOverTime(string userId)
    {
        var data = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null && s.Exercise.DifficultyLevel != null)
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => new {
                Date = g.Key.ToString("yyyy-MM-dd"),
                AverageDifficulty = g.Average(s => s.Exercise.DifficultyLevel.Id)  // Possivelmente criar uma propriedade com o valor numerico do nível de dificuldade
            })
            .OrderBy(x => x.Date)
            .ToList();
        return data;
    }
    
    public IDictionary<string, int> GetNewExercisesPerWeek(string userId)
    {
        var firstAttempts = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .GroupBy(s => s.Exercise.ExerciseId)
            .Select(g => g.OrderBy(s => s.Timestamp).FirstOrDefault())
            .Where(s => s != null)
            .ToList();

        var result = firstAttempts
            .GroupBy(s => new {
                s.Timestamp.Year,
                Week = System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                    s.Timestamp, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)
            })
            .Select(g => new {
                WeekLabel = $"{g.Key.Year}-W{g.Key.Week}",
                Count = g.Count()
            })
            .ToDictionary(x => x.WeekLabel, x => x.Count);

        return result;
    }
    
    public object GetFirstAndLastExerciseAttempted(string userId)
    {
        var firstAttempt = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .OrderBy(s => s.Timestamp)
            .Select(s => s.Exercise.Name)
            .FirstOrDefault();
 
        var lastAttempt = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .OrderByDescending(s => s.Timestamp)
            .Select(s => s.Exercise.Name)
            .FirstOrDefault();
 
        return new { FirstExercise = firstAttempt, LastExercise = lastAttempt };
    }
    
    public string GetExerciseToReview(string userId)
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
    
    public string GetNextRecommendedExercise(string userId)
    {
        var singleAttemptExercises = _context.Scores
            .Where(s => s.UserId == userId && s.Exercise != null)
            .GroupBy(s => new { s.ExerciseId, s.Exercise.Name })
            .Where(g => g.Count() == 1)
            .Select(g => new {
                Exercise = g.Key.Name,
                LastAttempt = g.Max(s => s.Timestamp),
                ErrorCount = g.Sum(s => s.ErrorCount)
            })
            .OrderByDescending(x => x.LastAttempt)
            .ThenByDescending(x => x.ErrorCount)
            .FirstOrDefault();
        return singleAttemptExercises?.Exercise;
    }
    
    public string SuggestDifficultyIncrease(string userId)
    {
        double overallAccuracy = GetOverallAccuracy(userId);
        if (overallAccuracy >= 90)
        {
            return "Sua acurácia está alta. Considere aumentar a dificuldade.";
        }
        return "Continue praticando no seu nível atual.";
    }
    
    public List<string> GetExercisesAboveAverage(string userId)
    {
        var scores = _context.Scores.Where(s => s.UserId == userId && s.Exercise != null).ToList();
        int totalCorrect = scores.Sum(s => s.CorrectCount);
        int totalAttempts = scores.Sum(s => s.CorrectCount + s.ErrorCount);
        double overallAccuracy = totalAttempts > 0 ? (double)totalCorrect / totalAttempts * 100 : 0;

        var exerciseAccuracies = scores
            .GroupBy(s => s.Exercise.Name)
            .Select(g => new {
                Exercise = g.Key,
                Accuracy = g.Sum(s => s.CorrectCount + s.ErrorCount) > 0
                    ? (double)g.Sum(s => s.CorrectCount) / (g.Sum(s => s.CorrectCount + s.ErrorCount)) * 100
                    : 0
            })
            .Where(x => x.Accuracy > overallAccuracy)
            .Select(x => x.Exercise)
            .ToList();
        return exerciseAccuracies;
    }
    
    public string GetBestPerformanceDay(string userId)
    {
        var bestDay = _context.Scores
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => new {
                Date = g.Key,
                NetScore = g.Sum(s => s.CorrectCount - s.ErrorCount)
            })
            .OrderByDescending(x => x.NetScore)
            .FirstOrDefault();
        return bestDay?.Date.ToString("yyyy-MM-dd");
    }
}
