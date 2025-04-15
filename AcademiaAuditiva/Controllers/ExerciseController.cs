using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
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
		
		[HttpGet]
		[HttpPost]
		public IActionResult RequestPlay([FromBody] PlayRequestDto request)
		{
			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound("Exercise not found.");

			var result = MusicTheoryService.GenerateNoteForExercise(exercise, request.Filters ?? new Dictionary<string, string>());

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

			bool isCorrect = false;

			switch (exercise.Name)
			{
				case "GuessNote":
					isCorrect = MusicTheoryService.NotesAreEquivalent(dto.UserGuess, dto.ActualAnswer);
					break;
				case "GuessChord":
					isCorrect = MusicTheoryService.AnswersAreEquivalent(dto.UserGuess, dto.ActualAnswer);
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
				answer = dto.ActualAnswer,
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

			var model = exercise.ToViewModel();

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

			var model = exercise.ToViewModel();

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

			var model = exercise.ToViewModel();

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C3-C5",
				CustomFiltersHtml = $@"
					<div class='mb-3'>
						<label for='chordGroup' class='form-label'>Qualidade dos Acordes</label>
						<select id='chordGroup' name='chordGroup' class='form-select'>
							<option value='major'>Apenas Maiores</option>
							<option value='minor'>Apenas Menores</option>
							<option value='all'>Todos</option>
						</select>
					</div>"
			};


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

			var model = exercise.ToViewModel();

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C3-C5",
				CustomFiltersHtml = @"
			<div class='row'>
				<div class='col-md-6 mb-3'>
					<label for='keySelect' class='form-label'>Selecione o Tom</label>
					<select id='keySelect' class='form-select'>
						<option value='C'>C</option>
						<option value='C#'>C#</option>
						<option value='D'>D</option>
						<option value='D#'>D#</option>
						<option value='E'>E</option>
						<option value='F'>F</option>
						<option value='F#'>F#</option>
						<option value='G'>G</option>
						<option value='G#'>G#</option>
						<option value='A'>A</option>
						<option value='A#'>A#</option>
						<option value='B'>B</option>
					</select>
				</div>
				<div class='col-md-6 mb-3'>
					<label for='scaleTypeSelect' class='form-label'>Qualidade da Tonalidade</label>
					<select id='scaleTypeSelect' class='form-select'>
						<option value='major'>Maior</option>
						<option value='minor'>Menor</option>
					</select>
				</div>
			</div>"
			};

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

			var model = exercise.ToViewModel();

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C3-C5",
				CustomFiltersHtml = @"
			<div class='row'>
				<div class='col-md-6 mb-3'>
					<label for='keySelect' class='form-label'>Selecione o Tom</label>
					<select id='keySelect' class='form-select'>
						<option value='C'>C</option>
						<option value='D'>D</option>
						<option value='E'>E</option>
						<option value='F'>F</option>
						<option value='G'>G</option>
						<option value='A'>A</option>
						<option value='B'>B</option>
					</select>
				</div>
				<div class='col-md-6 mb-3'>
					<label for='intervalDirection' class='form-label'>Direção</label>
					<select id='intervalDirection' class='form-select'>
						<option value='asc'>Ascendente</option>
						<option value='desc'>Descendente</option>
					</select>
				</div>
			</div>"
			};

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

			var model = exercise.ToViewModel();

			model.Title = _localizer["Exercise.MissingNote.Title"];
			model.Instructions = _localizer["Exercise.MissingNote.Instructions"];
			model.Tips = new List<string>
			{
				_localizer["Exercise.MissingNote.Tip1"],
				_localizer["Exercise.MissingNote.Tip2"]
			};

			model.Filters = new ExerciseFiltersViewModel
			{
				Instrument = "Piano",
				Range = "C4-C5",
				CustomFiltersHtml = @"
			<div class='mb-3'>
				<label for='melodyLength' class='form-label'>Comprimento da Melodia</label>
				<select id='melodyLength' class='form-select'>
					<option value='4'>4</option>
					<option value='5'>5</option>
					<option value='6'>6</option>
					<option value='7'>7</option>
					<option value='8'>8</option>
				</select>
			</div>"
			};

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
