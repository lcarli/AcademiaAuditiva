using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AcademiaAuditiva.Controllers
{
    [Authorize]
    public class ExerciseController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ExerciseController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }


        #region GuessNote
        public IActionResult GuessNote()
        {
            int bestScore = _context.Scores
                           .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                           .OrderByDescending(s => s.CorrectCount - s.ErrorCount)
                           .FirstOrDefault()?.CorrectCount -
                           _context.Scores
                           .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                           .OrderByDescending(s => s.CorrectCount - s.ErrorCount)
                           .FirstOrDefault()?.ErrorCount ?? 0;

            ViewBag.BestScore = bestScore;
            return View();
        }


        [HttpPost]
        public IActionResult GuessNoteSaveScore(int correctCount, int errorCount)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Verifique se o usuário está logado
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Usuário não está logado." });
            }

            // Obtenha o ExerciseId para "GuessNote"
            var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessNote");
            if (exercise == null)
            {
                return Json(new { success = false, message = "Exercício GuessNote não encontrado." });
            }

            // Calcula o score atual
            int currentScore = correctCount - errorCount;

            // Obter a melhor pontuação anterior do usuário para o exercício GuessNote
            int bestScore = _context.Scores
                           .Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
                           .OrderByDescending(s => s.BestScore)
                           .FirstOrDefault()?.BestScore ?? 0;

            // Verifique se o score atual é melhor que o bestScore
            if (currentScore > bestScore)
            {
                _context.Scores.Add(new Score
                {
                    UserId = userId,
                    ExerciseId = exercise.ExerciseId,
                    CorrectCount = correctCount,
                    ErrorCount = errorCount,
                    BestScore = currentScore
                });
                _context.SaveChanges();

                return Json(new { success = true, message = "Nova melhor pontuação registrada!" });
            }

            return Json(new { success = true, message = "Pontuação não superou a anterior. Não foi registrada." });
        }
        #endregion

        #region GuessChord
        public IActionResult GuessChords()
        {
            int bestScore = _context.Scores
                           .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessChords")
                           .OrderByDescending(s => s.BestScore)
                           .FirstOrDefault()?.BestScore ?? 0;

            ViewBag.BestScore = bestScore;
            return View();
        }

        [HttpPost]
        public IActionResult GuessChordsSaveScore(int correctCount, int errorCount)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Verifique se o usuário está logado
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Usuário não está logado." });
            }

            // Obtenha o ExerciseId para "GuessChords"
            var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessChords");
            if (exercise == null)
            {
                return Json(new { success = false, message = "Exercício GuessChords não encontrado." });
            }

            int currentScore = correctCount - errorCount;

            // Pegue o best score atual para o usuário neste exercício
            var userBestScoreRecord = _context.Scores
                                              .Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
                                              .OrderByDescending(s => s.BestScore)
                                              .FirstOrDefault();

            int userBestScore = userBestScoreRecord != null ? userBestScoreRecord.CorrectCount - userBestScoreRecord.ErrorCount : int.MinValue;

            // Salve apenas se o score atual for melhor que o melhor score do usuário para esse exercício
            if (currentScore > userBestScore)
            {
                _context.Scores.Add(new Score
                {
                    UserId = userId,
                    ExerciseId = exercise.ExerciseId,
                    CorrectCount = correctCount,
                    ErrorCount = errorCount,
                    BestScore = currentScore
                });
                _context.SaveChanges();
                return Json(new { success = true, message = "Novo recorde!" });
            }

            return Json(new { success = false, message = "Não superou o recorde anterior." });
        }
		#endregion

		#region GuessInterval
		public IActionResult GuessInterval()
		{
			int bestScore = _context.Scores
						   .Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && s.Exercise.Name == "GuessInterval")
						   .OrderByDescending(s => s.BestScore)
						   .FirstOrDefault()?.BestScore ?? 0;

			ViewBag.BestScore = bestScore;
			return View();
		}

		[HttpPost]
		public IActionResult GuessIntervalSaveScore(int correctCount, int errorCount)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			// Verifique se o usuário está logado
			if (string.IsNullOrEmpty(userId))
			{
				return Json(new { success = false, message = "Usuário não está logado." });
			}

			// Obtenha o ExerciseId para "GuessInterval"
			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessInterval");
			if (exercise == null)
			{
				return Json(new { success = false, message = "Exercício GuessInterval não encontrado." });
			}

			int currentScore = correctCount - errorCount;

			// Pegue o best score atual para o usuário neste exercício
			var userBestScoreRecord = _context.Scores
											  .Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
											  .OrderByDescending(s => s.BestScore)
											  .FirstOrDefault();

			int userBestScore = userBestScoreRecord != null ? userBestScoreRecord.CorrectCount - userBestScoreRecord.ErrorCount : int.MinValue;

			// Salve apenas se o score atual for melhor que o melhor score do usuário para esse exercício
			if (currentScore > userBestScore)
			{
				_context.Scores.Add(new Score
				{
					UserId = userId,
					ExerciseId = exercise.ExerciseId,
					CorrectCount = correctCount,
					ErrorCount = errorCount,
					BestScore = currentScore
				});
				_context.SaveChanges();
				return Json(new { success = true, message = "Novo recorde!" });
			}

			return Json(new { success = false, message = "Não superou o recorde anterior." });
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
		public IActionResult GuessQualityChordsSaveScore(int correctCount, int errorCount)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			// Verifique se o usuário está logado
			if (string.IsNullOrEmpty(userId))
			{
				return Json(new { success = false, message = "Usuário não está logado." });
			}

			// Obtenha o ExerciseId para "GuessQuality"
			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessQuality");
			if (exercise == null)
			{
				return Json(new { success = false, message = "Exercício GuessQuality não encontrado." });
			}

			int currentScore = correctCount - errorCount;

			// Pegue o best score atual para o usuário neste exercício
			var userBestScoreRecord = _context.Scores
											  .Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
											  .OrderByDescending(s => s.BestScore)
											  .FirstOrDefault();

			int userBestScore = userBestScoreRecord != null ? userBestScoreRecord.CorrectCount - userBestScoreRecord.ErrorCount : int.MinValue;

			// Salve apenas se o score atual for melhor que o melhor score do usuário para esse exercício
			if (currentScore > userBestScore)
			{
				_context.Scores.Add(new Score
				{
					UserId = userId,
					ExerciseId = exercise.ExerciseId,
					CorrectCount = correctCount,
					ErrorCount = errorCount,
					BestScore = currentScore
				});
				_context.SaveChanges();
				return Json(new { success = true, message = "Novo recorde!" });
			}

			return Json(new { success = false, message = "Não superou o recorde anterior." });
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
		public IActionResult GuessFunctionSaveScore(int correctCount, int errorCount)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			// Verifique se o usuário está logado
			if (string.IsNullOrEmpty(userId))
			{
				return Json(new { success = false, message = "Usuário não está logado." });
			}

			// Obtenha o ExerciseId para "GuessFunction"
			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessFunction");
			if (exercise == null)
			{
				return Json(new { success = false, message = "Exercício GuessFunction não encontrado." });
			}

			int currentScore = correctCount - errorCount;

			var userBestScoreRecord = _context.Scores
											  .Where(s => s.UserId == userId && s.ExerciseId == exercise.ExerciseId)
											  .OrderByDescending(s => s.BestScore)
											  .FirstOrDefault();

			int userBestScore = userBestScoreRecord != null ? userBestScoreRecord.CorrectCount - userBestScoreRecord.ErrorCount : int.MinValue;

			if (currentScore > userBestScore)
			{
				_context.Scores.Add(new Score
				{
					UserId = userId,
					ExerciseId = exercise.ExerciseId,
					CorrectCount = correctCount,
					ErrorCount = errorCount,
					BestScore = currentScore
				});
				_context.SaveChanges();
				return Json(new { success = true, message = "Novo recorde!" });
			}

			return Json(new { success = false, message = "Não superou o recorde anterior." });
		}
		#endregion

	}
}
