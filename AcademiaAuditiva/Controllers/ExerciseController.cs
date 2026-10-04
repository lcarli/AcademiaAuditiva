using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using AcademiaAuditiva.Services.Scoring;
using AcademiaAuditiva.ViewModels;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
	[AutoValidateAntiforgeryToken]
	public class ExerciseController : Controller
	{
		private readonly ApplicationDbContext _context;
		private readonly IStringLocalizer<SharedResources> _localizer;
		private readonly IAnalyticsService _analyticsService;
		private readonly IExerciseValidatorRegistry _validators;
		private readonly IMusicTheoryService _musicTheory;
		private readonly IDistributedCache _cache;
		private readonly IAudioTokenService _audioTokens;
		private readonly IAudioMixerService _audioMixer;
		private readonly AcademiaAuditiva.Services.Audio.ExercisePlaybackPlanner _playbackPlanner;
		private readonly IGamificationService _gamification;
		private readonly ILearningPathService _learningPath;
		private readonly ILogger<ExerciseController> _logger;
		// Expected-answer entries live for one round (15 min) and are
		// keyed per (user, exercise). The cache is a distributed abstraction
		// so scale-out works once Redis is wired in.
		private static readonly DistributedCacheEntryOptions _expectedAnswerTtl =
			new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

		public ExerciseController(
			ApplicationDbContext context,
			IStringLocalizer<SharedResources> localizer,
			IAnalyticsService analyticsService,
			IExerciseValidatorRegistry validators,
			IMusicTheoryService musicTheory,
			IDistributedCache cache,
			IAudioTokenService audioTokens,
			IAudioMixerService audioMixer,
			AcademiaAuditiva.Services.Audio.ExercisePlaybackPlanner playbackPlanner,
			IGamificationService gamification,
			ILearningPathService learningPath,
			ILogger<ExerciseController> logger)
		{
			_context = context;
			_localizer = localizer;
			_analyticsService = analyticsService;
			_validators = validators;
			_musicTheory = musicTheory;
			_cache = cache;
			_audioTokens = audioTokens;
			_audioMixer = audioMixer;
			_playbackPlanner = playbackPlanner;
			_gamification = gamification;
			_learningPath = learningPath;
			_logger = logger;
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
		[EnableRateLimiting("RequestPlay")]
		// Legit payloads are ~100 bytes (exerciseId + a few filters).
		[RequestSizeLimit(8 * 1024)]
		public async Task<IActionResult> RequestPlay([FromBody] PlayRequestDto request)
		{
			if (request is null)
				return BadRequest();

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			var filters = request.Filters ?? new Dictionary<string, string>();

			var instrument = Request.Cookies["instrument"] ?? "Piano";
			var noteRange = Request.Cookies["noteRange"];

			if (!filters.ContainsKey("instrument"))
				filters["instrument"] = instrument;

			if (!filters.ContainsKey("guitarPosition") && Request.Cookies["guitarPosition"] is { } guitarPosition)
				filters["guitarPosition"] = guitarPosition;

			// With no range, the planner plays the octave the sliders start on.
			if (!filters.ContainsKey("noteRange") && noteRange is not null)
				filters["noteRange"] = noteRange;

			var plan = _playbackPlanner.Plan(exercise, filters);

			// SolfegeMelody shows its melody as sheet music for the student
			// to sing, so it gets no audio token: the expected answer is
			// cached for ValidateExercise and the melody is returned in
			// clear text for the staff renderer.
			if (plan.PlaybackPlans.Count == 0)
			{
				var sessionData = new ExerciseSessionData { ExpectedAnswer = plan.ExpectedAnswerJson, Free = request.Free };
				await _cache.SetStringAsync(
					ExpectedAnswerCacheKey(userId, request.ExerciseId),
					JsonConvert.SerializeObject(sessionData),
					_expectedAnswerTtl);
				// Sent verbatim: Json() uses System.Text.Json, which writes every value of a
				// Newtonsoft JObject as an empty array.
				return Content(plan.ExpectedAnswerJson, "application/json");
			}

			// Mix every plan into a single playable blob, then collect
			// "container/blobName" strings so the token service stores
			// only opaque references.
			var mixedAddresses = new string[plan.PlaybackPlans.Count];
			for (var i = 0; i < plan.PlaybackPlans.Count; i++)
			{
				var mixed = await _audioMixer.MixAsync(plan.PlaybackPlans[i], HttpContext.RequestAborted);
				mixedAddresses[i] = $"{mixed.Container}/{mixed.BlobName}";
			}

			var round = await _audioTokens.CreateRoundAsync(
				userId,
				request.ExerciseId,
				plan.ExpectedAnswerJson,
				mixedAddresses,
				free: request.Free,
				cancellationToken: HttpContext.RequestAborted);

			// Uniform response: most exercises ship one play token; only
			// GuessMissingNote ships two (melody1Token, melody2Token).
			// Staff-based exercises also need a `metadata` payload so the
			// front-end can pre-render the prompt notes / staff context
			// without leaking the full answer.
			var staffExercises = new HashSet<string> {
				"CompleteScale", "CompleteChord", "TransposeScale",
				"MelodicDictation", "RhythmDictation"
			};
			object response;
			if (exercise.Name == "GuessMissingNote")
			{
				response = new { roundId = round.RoundId, melody1Token = round.Tokens[0], melody2Token = round.Tokens[1] };
			}
			else if (staffExercises.Contains(exercise.Name))
			{
				// Build a plain CLR dictionary because the action returns via
				// System.Text.Json (no AddNewtonsoftJson is registered) which
				// cannot serialize a Newtonsoft JObject as a real JSON object.
				var expected = JObject.Parse(plan.ExpectedAnswerJson);
				var metadata = new Dictionary<string, object?>();
				foreach (var field in new[] {
					"promptNotes", "clef", "keySignature", "timeSignature",
					"numMeasures", "octave", "originalRoot", "targetRoot", "scale", "level",
					"root", "quality", "firstNote", "firstDuration"
				})
				{
					var token = expected[field];
					if (token != null && token.Type != JTokenType.Null)
					{
						metadata[field] = ToPlainJsonValue(token);
					}
				}
				response = new { roundId = round.RoundId, playToken = round.Tokens[0], metadata = metadata };
			}
			else
			{
				response = new { roundId = round.RoundId, playToken = round.Tokens[0] };
			}
			return Json(response);
		}

		private static object? ToPlainJsonValue(JToken token)
			=> token switch
			{
				JValue value => value.Value,
				JArray array => array.Select(ToPlainJsonValue).ToArray(),
				JObject obj => obj.Properties().ToDictionary(p => p.Name, p => ToPlainJsonValue(p.Value)),
				_ => token.ToString()
			};


		[HttpPost]
		public async Task<IActionResult> ValidateExercise([FromBody] ValidateExerciseDto dto)
		{
			if (dto is null)
				return BadRequest();

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value, isCorrect = false });

			var exercise = await _context.Exercises.FirstOrDefaultAsync(e => e.ExerciseId == dto.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			// Defensive guard: the auth cookie might survive across DB
			// resets (common in local dev when the dev container is
			// recreated). When the cookie's UserId no longer exists in
			// AspNetUsers, the score INSERTs would blow up on the FK
			// constraint and surface as an opaque 500. Bail out early
			// with a friendly message instead.
			var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
			if (!userExists)
				return Json(new { success = false, message = _localizer["Exercise.SessionExpired"].Value, isCorrect = false });

			// Resolve the expected answer either from the round (modern
			// audio-token flow) or, for sheet-music exercises that don't
			// produce a token, from the legacy session-key cache. The
			// round also says whether it is a free practice round: the
			// mode is fixed by RequestPlay, never by this request.
			string expectedAnswer = null;
			bool roundConsumed = false;
			bool free = false;

			if (!string.IsNullOrEmpty(dto.RoundId))
			{
				var round = await _audioTokens.GetRoundAsync(userId, dto.ExerciseId, dto.RoundId, HttpContext.RequestAborted);
				if (round is not null)
				{
					expectedAnswer = round.ExpectedAnswerJson;
					roundConsumed = true;
					free = round.Free;
				}
			}

			var legacyKey = ExpectedAnswerCacheKey(userId, dto.ExerciseId);
			if (expectedAnswer is null)
			{
				var json = await _cache.GetStringAsync(legacyKey);
				if (string.IsNullOrEmpty(json))
					return Json(new { success = false, message = _localizer["Exercise.SessionExpired"].Value, isCorrect = false });
				var legacy = JsonConvert.DeserializeObject<ExerciseSessionData>(json);
				expectedAnswer = legacy.ExpectedAnswer;
				free = legacy.Free;
			}

			var validator = _validators.Get(exercise.Name);
			if (validator == null)
			{
				return Json(new { success = false, message = _localizer["Exercise.NoValidator", exercise.Name].Value, isCorrect = false });
			}

			var validation = validator.Validate(dto.UserGuess, expectedAnswer);
			var isCorrect = validation.IsCorrect;
			var currentAnswer = validation.CanonicalAnswer;

			// One-shot semantics. Drop both the modern round (so the same
			// audio tokens can't be replayed against the just-checked
			// answer) and the legacy expected-answer key (used by the
			// sheet-music exercises that don't get a round).
			async Task ForgetRoundAsync()
			{
				if (roundConsumed)
				{
					await _audioTokens.RemoveRoundAsync(userId, dto.ExerciseId, dto.RoundId, HttpContext.RequestAborted);
				}
				await _cache.RemoveAsync(legacyKey);
			}

			// Free practice is checked and forgotten: no score, attempt log,
			// XP, badge, learning path or routine progress.
			if (free)
			{
				await ForgetRoundAsync();
				return Json(new
				{
					success = true,
					free = true,
					isCorrect,
					answer = currentAnswer,
					message = isCorrect ? _localizer["Exercise.CorrectAnswer"].Value : _localizer["Exercise.IncorrectAnswer"].Value
				});
			}

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

			await ForgetRoundAsync();
			
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

			// XP, level and badges are a bonus: a failure here must not lose the
			// answer that was already scored above.
			object? rewards = null;
			try
			{
				var attempt = await _gamification.RecordAttemptAsync(
					userId, isCorrect, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
				rewards = BuildRewards(attempt);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogWarning(ex, "Could not update XP and badges after exercise {ExerciseId}.", exercise.ExerciseId);
			}

			// Learning path progress is a bonus too, and independent of the rewards.
			object? path = null;
			if (LearningPathCatalog.Steps.Any(s => s.Exercise == exercise.Name))
			{
				try
				{
					var progress = await _learningPath.GetProgressAsync(userId, HttpContext.RequestAborted);
					path = BuildPathFeedback(progress, exercise.Name);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogWarning(ex, "Could not update the learning path after exercise {ExerciseId}.", exercise.ExerciseId);
				}
			}

			return Json(new
			{
				success = true,
				isCorrect,
				newCorrectCount = correctCount,
				newErrorCount = errorCount,
				bestScore,
				answer = currentAnswer,
				message = isCorrect ? _localizer["Exercise.CorrectAnswer"].Value : _localizer["Exercise.IncorrectAnswer"].Value,
				rewards,
				path
			});
		}

		/// <summary>
		/// Shows the answer of a free practice round without using the round up,
		/// so the student can listen again knowing what to hear, then answer it.
		/// A scored round never shows its answer before it is answered.
		/// </summary>
		[HttpPost]
		// Shares the RequestPlay budget: a reveal follows a play.
		[EnableRateLimiting("RequestPlay")]
		[RequestSizeLimit(8 * 1024)]
		public async Task<IActionResult> RevealAnswer([FromBody] RevealAnswerDto request)
		{
			if (request is null || string.IsNullOrEmpty(request.RoundId))
				return BadRequest();

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = await _context.Exercises.FirstOrDefaultAsync(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			var round = await _audioTokens.GetRoundAsync(userId, request.ExerciseId, request.RoundId, HttpContext.RequestAborted);
			if (round is null)
				return Json(new { success = false, message = _localizer["Exercise.SessionExpired"].Value });

			if (!round.Free)
				return StatusCode(StatusCodes.Status403Forbidden);

			var validator = _validators.Get(exercise.Name);
			if (validator == null)
				return Json(new { success = false, message = _localizer["Exercise.NoValidator", exercise.Name].Value });

			return Json(new { success = true, answer = validator.AnswerOf(round.ExpectedAnswerJson) });
		}

		// Shape read by wwwroot/js/core/rewards.js. Null unless the answered exercise is
		// the player's current step or the answer just completed its step.
		private object? BuildPathFeedback(LearningPathProgress progress, string exerciseName)
		{
			var label = _localizer["LearningPath.Title"].Value;
			var done = progress.JustCompleted;
			if (done is not null && done.Exercise == exerciseName)
			{
				var next = progress.Current;
				var (title, icon) = progress.IsComplete
					? (_localizer["LearningPath.PathComplete.Title"].Value, "bi-trophy")
					: progress.UnitOf(done).State == StepState.Completed
						? (_localizer["LearningPath.UnitComplete.Title"].Value, "bi-flag")
						: (_localizer["LearningPath.StepComplete.Title"].Value, "bi-check2-circle");
				return new
				{
					label,
					text = _localizer["LearningPath.StepComplete.Title"].Value,
					percent = 100,
					completed = true,
					celebration = new
					{
						title,
						icon,
						text = _localizer["LearningPath.StepCompleteText", done.Number, _localizer.StepTitle(done)].Value,
						nextText = next is null ? null : _localizer["LearningPath.NextStep", _localizer.StepTitle(next)].Value,
						actionText = next is null ? _localizer["LearningPath.ViewPath"].Value : _localizer["LearningPath.GoToNext"].Value,
						actionUrl = next is null ? Url.Action("Index", "LearningPath") : Url.StepUrl(next),
						closeText = _localizer["Gamification.Continue"].Value
					}
				};
			}

			var current = progress.Current;
			if (current is null || current.Exercise != exerciseName) return null;
			return new
			{
				label,
				text = _localizer["LearningPath.Progress", current.Correct, current.Required].Value,
				percent = current.Percent,
				completed = false,
				celebration = (object?)null
			};
		}

		// Shape read by wwwroot/js/core/rewards.js; every text is already localized.
		private object BuildRewards(AttemptRewards attempt)
		{
			var progress = attempt.Progress;
			var rankName = _localizer[$"Gamification.Rank.{progress.Rank}"].Value;
			var badges = attempt.NewBadges.Select(b => new
			{
				key = b.Key,
				title = b.Title,
				description = b.Description,
				image = Url.BadgeImage(b.Key),
				group = b.Group.ToString().ToLowerInvariant()
			}).ToList();

			string? badgesTitle = badges.Count switch
			{
				0 => null,
				1 => _localizer["Gamification.BadgeEarnedTitle"].Value,
				_ => _localizer["Gamification.BadgesEarnedTitle", badges.Count].Value
			};

			object? celebration = null;
			if (attempt.LevelUp || badges.Count > 0)
			{
				celebration = new
				{
					title = attempt.LevelUp ? _localizer["Gamification.LevelUpTitle"].Value : badgesTitle,
					text = attempt.LevelUp ? _localizer["Gamification.LevelUpText", progress.Level, rankName].Value : null,
					badgesHeading = attempt.LevelUp ? badgesTitle : null,
					viewAllText = _localizer["Gamification.ViewAll"].Value,
					viewAllUrl = Url.Action("Achievements", "Dashboard"),
					closeText = _localizer["Gamification.Continue"].Value
				};
			}

			return new
			{
				xp = progress.Xp,
				xpGained = attempt.XpGained,
				xpGainedText = _localizer["Gamification.XpGained", attempt.XpGained].Value,
				level = progress.Level,
				levelText = _localizer["Gamification.Level", progress.Level].Value,
				levelPercent = progress.LevelPercent,
				levelUp = attempt.LevelUp,
				rank = new { symbol = progress.Rank, name = rankName },
				badges,
				celebration
			};
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

		#region HigherOrLower
		public IActionResult HigherOrLower()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "HigherOrLower");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region GuessScaleType
		public IActionResult GuessScaleType()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessScaleType");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region GuessGreekMode
		public IActionResult GuessGreekMode()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessGreekMode");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region GuessCadence
		public IActionResult GuessCadence()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessCadence");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region GuessInversion
		public IActionResult GuessInversion()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "GuessInversion");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region CompleteScale
		public IActionResult CompleteScale()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "CompleteScale");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region CompleteChord
		public IActionResult CompleteChord()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "CompleteChord");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region TransposeScale
		public IActionResult TransposeScale()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "TransposeScale");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region MelodicDictation
		public IActionResult MelodicDictation()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "MelodicDictation");
			if (exercise == null)
				return NotFound();

			var model = exercise.ToViewModel(_localizer);

			return View(model);
		}
		#endregion

		#region RhythmDictation
		public IActionResult RhythmDictation()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var exercise = _context.Exercises.FirstOrDefault(e => e.Name == "RhythmDictation");
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
