using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Scoring;
using AcademiaAuditiva.ViewModels;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Controllers
{
	[Authorize]
	public class ExerciseController : Controller
	{
		private readonly ApplicationDbContext _context;
		private readonly IStringLocalizer<SharedResources> _localizer;
		private readonly IAnalyticsService _analyticsService;
		private readonly IExerciseValidatorRegistry _validators;
		private readonly IMusicTheoryService _musicTheory;
		private readonly IDistributedCache _cache;

		// Expected-answer entries live for one round (15 min) and are
		// keyed per (user, exercise). The cache is a distributed abstraction
		// so scale-out works once Redis is wired in.
		private static readonly DistributedCacheEntryOptions _expectedAnswerTtl =
			new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

		public ExerciseController(ApplicationDbContext context, IStringLocalizer<SharedResources> localizer, IAnalyticsService analyticsService, IExerciseValidatorRegistry validators, IMusicTheoryService musicTheory, IDistributedCache cache)
		{
			_context = context;
			_localizer = localizer;
			_analyticsService = analyticsService;
			_validators = validators;
			_musicTheory = musicTheory;
			_cache = cache;
		}

		private static string ExpectedAnswerCacheKey(string userId, int exerciseId)
			=> $"ExerciseAnswer:{userId}:{exerciseId}";

		public async Task<IActionResult> Index()
		{
			var exercises = _context.Exercises.ToList();
			var difficulties = await _context.DifficultyLevels.OrderBy(d => d.Id).ToListAsync();
			ViewBag.DifficultyLevels = difficulties;
			var exerciseTypes = await _context.ExerciseTypes.OrderBy(t => t.Id).ToListAsync();
			ViewBag.ExerciseTypes = exerciseTypes;
			return View(exercises);
		}

		#region General Play and Validate
		
		[HttpPost]
		public async Task<IActionResult> RequestPlay([FromBody] PlayRequestDto request)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			var filters = request.Filters ?? new Dictionary<string, string>();

			var instrument = Request.Cookies["instrument"] ?? "Piano";
			var noteRange = Request.Cookies["noteRange"] ?? "C4-C4";

			if (!filters.ContainsKey("instrument"))
				filters["instrument"] = instrument;

			if (!filters.ContainsKey("noteRange"))
				filters["noteRange"] = noteRange;

			var result = _musicTheory.GenerateNoteForExercise(exercise, filters);

			var sessionData = new ExerciseSessionData
			{
				ExpectedAnswer = JsonConvert.SerializeObject(result)
			};

			await _cache.SetStringAsync(
				ExpectedAnswerCacheKey(userId, request.ExerciseId),
				JsonConvert.SerializeObject(sessionData),
				_expectedAnswerTtl);

			return Json(result);
		}


		[HttpPost]
		public async Task<IActionResult> ValidateExercise([FromBody] ValidateExerciseDto dto)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = "User not logged in.", isCorrect = false });

			var exercise = await _context.Exercises.FirstOrDefaultAsync(e => e.ExerciseId == dto.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			var sessionKey = ExpectedAnswerCacheKey(userId, dto.ExerciseId);
			var json = await _cache.GetStringAsync(sessionKey);

			if (string.IsNullOrEmpty(json))
				return Json(new { success = false, message = _localizer["Exercise.SessionExpired"].Value, isCorrect = false });

			var sessionData = JsonConvert.DeserializeObject<ExerciseSessionData>(json);
			var expectedAnswer = sessionData.ExpectedAnswer;

			var validator = _validators.Get(exercise.Name);
			if (validator == null)
			{
				return Json(new { success = false, message = _localizer["Exercise.NoValidator", exercise.Name].Value, isCorrect = false });
			}

			var validation = validator.Validate(dto.UserGuess, sessionData.ExpectedAnswer);
			var isCorrect = validation.IsCorrect;
			var currentAnswer = validation.CanonicalAnswer;

			var existingScore = await _context.Scores
				.Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
				.OrderByDescending(s => s.Timestamp)
				.FirstOrDefaultAsync();

			int prevCorrect = existingScore?.CorrectCount ?? 0;
			int prevError = existingScore?.ErrorCount ?? 0;
			int prevBest = existingScore?.BestScore ?? 0;

			var update = ScoreAggregator.Apply(prevCorrect, prevError, prevBest, isCorrect);
			int correctCount = update.CorrectCount;
			int errorCount = update.ErrorCount;
			int bestScore = update.BestScore;

			var now = DateTime.UtcNow;

			// Legacy Score row (running totals; kept until the contract step
			// of the score-model split drops the table).
			_context.Scores.Add(new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = bestScore,
				TimeSpentSeconds = dto.TimeSpentSeconds,
				Timestamp = now
			});

			// New per-attempt snapshot.
			_context.ScoreSnapshots.Add(new ScoreSnapshot
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				IsCorrect = isCorrect,
				TimeSpentSeconds = dto.TimeSpentSeconds,
				Timestamp = now
			});

			// Upsert the aggregate row (one per user+exercise).
			var aggregate = await _context.ScoreAggregates
				.FirstOrDefaultAsync(a => a.UserId == userId && a.ExerciseId == exercise.ExerciseId);
			if (aggregate == null)
			{
				_context.ScoreAggregates.Add(new ScoreAggregate
				{
					UserId = userId,
					ExerciseId = exercise.ExerciseId,
					CorrectCount = correctCount,
					ErrorCount = errorCount,
					BestScore = bestScore,
					LastAttemptAt = now
				});
			}
			else
			{
				aggregate.CorrectCount = correctCount;
				aggregate.ErrorCount = errorCount;
				aggregate.BestScore = bestScore;
				aggregate.LastAttemptAt = now;
			}

			await _context.SaveChangesAsync();

			// One-shot semantics: drop the expected-answer entry so the
			// same round can't be replayed against the same cached answer.
			await _cache.RemoveAsync(sessionKey);
			
			await _analyticsService.SaveAttemptAsync(new ExerciseAttemptLog
			{
				UserId = userId,
				Exercise = exercise.Name,
				Timestamp = DateTime.UtcNow,
				QuestionId = currentAnswer,
				Attempt = new AttemptDetails
				{
					UserAnswer = dto.UserGuess,
					ExpectedAnswer = currentAnswer,
					IsCorrect = isCorrect,
					TimeSpentSeconds = dto.TimeSpentSeconds,
				}
			});

			return Json(new
			{
				success = true,
				isCorrect,
				newCorrectCount = correctCount,
				newErrorCount = errorCount,
				bestScore,
				answer = currentAnswer,
				message = isCorrect ? _localizer["Exercise.CorrectAnswer"].Value : _localizer["Exercise.IncorrectAnswer"].Value
			});
		}

		#endregion

		#region GuessNote
		public IActionResult GuessNote()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessNote");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		#endregion

		#region GuessChord
		public IActionResult GuessChords()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessChords");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region IntervalMelodico
		public IActionResult IntervalMelodico()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "IntervalMelodico");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region GuessInterval
		public IActionResult GuessInterval()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessInterval");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessIntervalSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessInterval");
			if (exercise == null)
				return Json(new { success = false, message = _localizer["Exercise.NotFound"].Value });

			int currentScore = Math.Max(0, correctCount - errorCount);

			var newScore = new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = currentScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = DateTime.UtcNow
			};

			_context.Scores.Add(newScore);
			_context.SaveChanges();

			return Json(new { success = true, message = _localizer["Exercise.ScoreSaved"].Value });
		}

		#endregion

		#region GuessQuality

		public IActionResult GuessQuality()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessQuality");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessQualitySaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessQuality");
			if (exercise == null)
				return Json(new { success = false, message = _localizer["Exercise.NotFound"].Value });

			int currentScore = Math.Max(0, correctCount - errorCount);

			var newScore = new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = currentScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = DateTime.UtcNow
			};

			_context.Scores.Add(newScore);
			_context.SaveChanges();

			return Json(new { success = true, message = _localizer["Exercise.ScoreSaved"].Value });
		}

		#endregion

		#region GuessFunction
		public IActionResult GuessFunction()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			
			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFunction");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessFunctionSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFunction");
			if (exercise == null)
				return Json(new { success = false, message = _localizer["Exercise.NotFound"].Value });

			int currentScore = Math.Max(0, correctCount - errorCount);

			var newScore = new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = currentScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = DateTime.UtcNow
			};

			_context.Scores.Add(newScore);
			_context.SaveChanges();

			return Json(new { success = true, message = _localizer["Exercise.ScoreSaved"].Value });
		}

		#endregion

		#region GuessFullInterval

		public IActionResult GuessFullInterval()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFullInterval");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessFullIntervalSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFullInterval");
			if (exercise == null)
				return Json(new { success = false, message = _localizer["Exercise.NotFound"].Value });

			int currentScore = Math.Max(0, correctCount - errorCount);

			var newScore = new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = currentScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = DateTime.UtcNow
			};

			_context.Scores.Add(newScore);
			_context.SaveChanges();

			return Json(new { success = true, message = _localizer["Exercise.ScoreSaved"].Value });
		}

		#endregion

		#region GuessMissingNote
		public IActionResult GuessMissingNote()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessMissingNote");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessMissingNoteSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
			{
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });
			}

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessMissingNote");
			if (exercise == null)
			{
				return Json(new { success = false, message = _localizer["Exercise.NotFound"].Value });
			}

			int currentScore = Math.Max(0, correctCount - errorCount);

			var newScore = new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = currentScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = DateTime.UtcNow
			};

			_context.Scores.Add(newScore);
			_context.SaveChanges();

			return Json(new { success = true, message = _localizer["Exercise.ScoreSaved"].Value });
		}

		#endregion

		#region SolfegeMelody
		public IActionResult SolfegeMelody()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "SolfegeMelody");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}

		#endregion
	}
}
