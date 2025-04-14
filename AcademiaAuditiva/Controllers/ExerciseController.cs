using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Security.Claims;

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

			var model = exercise.ToViewModel();

			model.AnswerOptions = new List<string>
			{
				"C4", "Cs4", "D4", "Ds4", "E4", "F4",
				"Fs4", "G4", "Gs4", "A4", "As4", "B4"
			};

			return View(model);
		}

		[HttpPost]
		public IActionResult GuessNoteSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessNote");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessNote não encontrado." });

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

			return Json(new { success = true, message = "Sessão registrada com sucesso!" });
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


			var model = exercise.ToViewModel();

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C4-C5",
				CustomFiltersHtml = $@"
					<div class='mb-3'>
						<label for='chordType' class='form-label'>{_localizer["Exercise.TypeChord"]}</label>
						<select id='chordType' name='chordType' class='form-select'>
							<option value='major'>{_localizer["Exercise.TypeChordMajeur"]}</option>
							<option value='minor'>{_localizer["Exercise.TypeChordMineur"]}</option>
							<option value='both'>{_localizer["Exercise.TypeChordMajeurMineur"]}</option>
							<option value='all'>{_localizer["Exercise.TypeChordAll"]}</option>
						</select>
					</div>"
			};



			model.AnswerOptions = new List<string>
			{
				"C", "C#", "D", "D#", "E", "F",
				"F#", "G", "G#", "A", "A#", "B"
			};

			return View(model);
		}


		[HttpPost]
		public IActionResult GuessChordsSaveScore(int correctCount, int errorCount, int timeSpentSeconds)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = "Usuário não está logado." });

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessChords");
			if (exercise == null)
				return Json(new { success = false, message = "Exercício GuessChords não encontrado." });

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

			return Json(new { success = true, message = "Sessão de acordes registrada com sucesso!" });
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

			var model = exercise.ToViewModel();

			model.Title = _localizer["Exercise.Interval.Title"];
			model.AnswerOptions = new List<string> {
				"2m", "2M", "3m", "3M", "4J", "5J", "6m", "6M", "7m", "7M", "8J"
			};

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C3-C5",
				CustomFiltersHtml = @"
					<div class='row'>
						<div class='col-md-6 mb-3'>
							<label for='keySelect' class='form-label'>Selecione o Tom</label>
							<select id='keySelect' class='form-select'>
								<option value='C4'>C</option>
								<option value='C#4'>C#</option>
								<option value='D4'>D</option>
								<option value='D#4'>D#</option>
								<option value='E4'>E</option>
								<option value='F4'>F</option>
								<option value='F#4'>F#</option>
								<option value='G4'>G</option>
								<option value='G#4'>G#</option>
								<option value='A4'>A</option>
								<option value='A#4'>A#</option>
								<option value='B4'>B</option>
							</select>
						</div>
						<div class='col-md-6 mb-3'>
							<label for='scaleTypeSelect' class='form-label'>Selecione a Escala</label>
							<select id='scaleTypeSelect' class='form-select'>
								<option value='major'>Maior</option>
								<option value='minor'>Menor</option>
							</select>
						</div>
					</div>
				"
			};

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
			int bestScore = _context.Scores
						   .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessQuality")
						   .OrderByDescending(s => s.BestScore)
						   .FirstOrDefault()?.BestScore ?? 0;

			ViewBag.BestScore = bestScore;
			return View();
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
			int bestScore = _context.Scores
						   .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessFunction")
						   .OrderByDescending(s => s.BestScore)
						   .FirstOrDefault()?.BestScore ?? 0;

			ViewBag.BestScore = bestScore;
			return View();
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
			int bestScore = _context.Scores
				.Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessFullInterval")
				.OrderByDescending(s => s.BestScore)
				.FirstOrDefault()?.BestScore ?? 0;

			ViewBag.BestScore = bestScore;
			return View();
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
			int bestScore = _context.Scores
				.Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessMissingNote")
				.OrderByDescending(s => s.BestScore)
				.FirstOrDefault()?.BestScore ?? 0;

			ViewBag.BestScore = bestScore;
			return View();
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
