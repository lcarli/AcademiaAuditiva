using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Controllers
{
	[Authorize]
	public class ExerciseController : Controller
	{
		private readonly ApplicationDbContext _context;
		private readonly IStringLocalizer<SharedResources> _localizer;

		public ExerciseController(ApplicationDbContext context, IStringLocalizer<SharedResources> localizer)
		{
			_context = context;
			_localizer = localizer;
		}

		public IActionResult Index()
		{
			var exercises = _context.Exercises.ToList();
			return View(exercises);
		}

		#region General Play and Validate
		
		[HttpPost]
		public IActionResult RequestPlay([FromBody] PlayRequestDto request)
		{
			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound("Exercise not found.");

			var filters = request.Filters ?? new Dictionary<string, string>();

			var instrument = Request.Cookies["instrument"] ?? "Piano";
			var noteRange = Request.Cookies["noteRange"] ?? "C4-C4";

			if (!filters.ContainsKey("instrument"))
				filters["instrument"] = instrument;

			if (!filters.ContainsKey("noteRange"))
				filters["noteRange"] = noteRange;

			var result = MusicTheoryService.GenerateNoteForExercise(exercise, filters);

			var sessionData = new ExerciseSessionData
			{
				ExpectedAnswer = JsonConvert.SerializeObject(result)
			};

			HttpContext.Session.SetString($"ExerciseAnswer_{request.ExerciseId}", JsonConvert.SerializeObject(sessionData));

			return Json(result);
		}


		[HttpPost]
		public IActionResult ValidateExercise([FromBody] ValidateExerciseDto dto)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = "User not logged in.", isCorrect = false });

			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == dto.ExerciseId);
			if (exercise == null)
				return NotFound("Exercício não encontrado.");

			var sessionKey = $"ExerciseAnswer_{dto.ExerciseId}";
			var json = HttpContext.Session.GetString(sessionKey);

			if (string.IsNullOrEmpty(json))
				return Json(new { success = false, message = "Sessão expirada ou resposta não encontrada.", isCorrect = false });

			var sessionData = JsonConvert.DeserializeObject<ExerciseSessionData>(json);
			var expectedAnswer = sessionData.ExpectedAnswer;

			bool isCorrect = false;
			var currentAnswer = "";

			switch (exercise.Name)
			{
				case "GuessNote":
					var objNote = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedNote = currentAnswer = (string)objNote["note"];
					isCorrect = MusicTheoryService.NotesAreEquivalent(dto.UserGuess, expectedNote);
					break;
				case "GuessChords":
					var objChord = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedRoot = (string)objChord["root"];
					var expectedQuality = (string)objChord["quality"];
					var actualChord = currentAnswer =  $"{expectedRoot}|{expectedQuality}";
					isCorrect = MusicTheoryService.AnswersAreEquivalent(dto.UserGuess, actualChord);
					break;
				case "GuessInterval":
					var objInterval = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedInterval = currentAnswer =  (string)objInterval["answer"];
					isCorrect = string.Equals(dto.UserGuess, expectedInterval, StringComparison.OrdinalIgnoreCase);
					break;
				case "GuessMissingNote":
					var objMelody = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedComparison = currentAnswer = (string)objMelody["answer"];
					isCorrect = string.Equals(dto.UserGuess, expectedComparison, StringComparison.OrdinalIgnoreCase);
					break;
				case "GuessFullInterval":
					var objFull = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedFull = currentAnswer = (string)objFull["answer"];
					isCorrect = string.Equals(dto.UserGuess, expectedFull, StringComparison.OrdinalIgnoreCase);
					break;
				case "GuessFunction":
					var objFunc = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedFunc = currentAnswer = (string)objFunc["answer"];
					isCorrect = string.Equals(dto.UserGuess, expectedFunc, StringComparison.OrdinalIgnoreCase);
					break;
				case "GuessQuality":
					var objQuality = JObject.Parse(sessionData.ExpectedAnswer);
					var expectedQuality2 = currentAnswer = (string)objQuality["answer"];
					isCorrect = string.Equals(dto.UserGuess, expectedQuality2, StringComparison.OrdinalIgnoreCase);
					break;
				default:
					break;
			}

			var existingScore = _context.Scores
				.Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
				.OrderByDescending(s => s.Timestamp)
				.FirstOrDefault();

			int correctCount = existingScore?.CorrectCount ?? 0;
			int errorCount = existingScore?.ErrorCount ?? 0;
			int bestScore = existingScore?.BestScore ?? 0;

			if (isCorrect) correctCount++;
			else errorCount++;

			int currentScore = correctCount - errorCount;
			if (currentScore > bestScore)
				bestScore = currentScore;

			_context.Scores.Add(new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = bestScore,
				TimeSpentSeconds = dto.TimeSpentSeconds,
				Timestamp = DateTime.UtcNow
			});
			_context.SaveChanges();
			

			return Json(new
			{
				success = true,
				isCorrect,
				newCorrectCount = correctCount,
				newErrorCount = errorCount,
				bestScore,
				answer = currentAnswer,
				message = isCorrect ? "Resposta correta!" : "Resposta incorreta."
			});
		}

		#endregion

		#region GuessNote
		public IActionResult GuessNote()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var bestScore = _context.Scores
							   .Where(s => s.UserId == userId)
							   .OrderByDescending(s => s.CorrectCount - s.ErrorCount)
							   .Select(s => s.CorrectCount - s.ErrorCount)
							   .FirstOrDefault();

			ViewBag.BestScore = bestScore;

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

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessChords")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;


			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessChords");
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

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessInterval")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;

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
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessInterval");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessInterval não encontrado." });

			int currentScore = correctCount - errorCount;

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

			return Json(new { success = true, message = "Sessão de intervalos registrada com sucesso!" });
		}

		#endregion

		#region GuessQuality

		public IActionResult GuessQuality()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessQuality")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;

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
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessQuality");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessQuality não encontrado." });

			int currentScore = correctCount - errorCount;

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

			return Json(new { success = true, message = "Sessão de qualidade de acordes registrada com sucesso!" });
		}

		#endregion

		#region GuessFunction
		public IActionResult GuessFunction()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessFunction")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;

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
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFunction");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessFunction não encontrado." });

			int currentScore = correctCount - errorCount;

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

			return Json(new { success = true, message = "Sessão de função harmônica registrada com sucesso!" });
		}

		#endregion

		#region GuessFullInterval

		public IActionResult GuessFullInterval()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessFullInterval")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;

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
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFullInterval");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessFullInterval não encontrado." });

			int currentScore = correctCount - errorCount;

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

			return Json(new { success = true, message = "Sessão de intervalos completos registrada com sucesso!" });
		}

		#endregion

		#region GuessMissingNote
		public IActionResult GuessMissingNote()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			int bestScore = _context.Scores
				.Where(s => s.UserId == userId && s.Exercise.Name == "GuessMissingNote")
				.OrderByDescending(s => s.BestScore)
				.Select(s => s.BestScore)
				.FirstOrDefault();

			ViewBag.BestScore = bestScore;

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
				return Json(new { success = false, message = "Usuário não está logado." });
			}

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessMissingNote");
			if (exercise == null)
			{
				return Json(new { success = false, message = "Exercício GuessMissingNote não encontrado." });
			}

			int currentScore = correctCount - errorCount;

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

			return Json(new { success = true, message = "Sessão de Missing Note registrada com sucesso!" });
		}

		#endregion
	}
}
