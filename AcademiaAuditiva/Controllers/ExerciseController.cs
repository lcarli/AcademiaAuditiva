using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using AcademiaAuditiva.Services.Routines;
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
		private readonly RoutineRounds _routines;
		private readonly TimeProvider _clock;
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
			RoutineRounds routines,
			TimeProvider clock,
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
			_routines = routines;
			_clock = clock;
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

			// A routine question names both its assignment and its item, and is never free practice.
			var routineLink = RoutineLink.From(request.RoutineAssignmentId, request.RoutineItemId);
			if ((request.RoutineAssignmentId is not null || request.RoutineItemId is not null)
				&& (routineLink is null || request.Free))
				return BadRequest();

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Json(new { success = false, message = _localizer["Exercise.UserNotLoggedIn"].Value });

			var exercise = _context.Exercises.FirstOrDefault(e => e.ExerciseId == request.ExerciseId);
			if (exercise == null)
				return NotFound(_localizer["Exercise.NotFound"].Value);

			var filters = request.Filters ?? new Dictionary<string, string>();

			RoutineRoundStatus? routineStatus = null;
			RoutineQuestion? routineQuestion = null;
			if (routineLink is { } link)
			{
				var routine = await _routines.FindAsync(userId, link, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
				if (routine is null || routine.Item.ExerciseId != exercise.ExerciseId)
				{
					var unavailable = RoutineRoundStatus.Unavailable(_localizer);
					return Json(new { success = false, message = unavailable.Blocked!.Message, routine = unavailable });
				}

				routineStatus = RoutineRoundStatus.From(routine.Item, _localizer);
				if (!routine.Item.IsPlayable)
					return Json(new { success = false, message = routineStatus.Blocked!.Message, routine = routineStatus });

				// Like a test, a question is asked again until it is answered: Play cannot skip it.
				// Once its question is answered, the pending one is stale.
				routineQuestion = new RoutineQuestion(link, routine.Item.NextQuestion);
				var pending = await _routines.PendingQuestionAsync(userId, link, HttpContext.RequestAborted);
				if (pending?.RoundId is { } pendingRoundId)
				{
					var pendingRound = await _audioTokens.GetRoundAsync(userId, exercise.ExerciseId, pendingRoundId, HttpContext.RequestAborted);
					if (pendingRound is not null && pendingRound.Routine == routineQuestion)
						return Json(PlayResponse(exercise, pendingRound, routineStatus));
				}
				else if (pending?.Sheet is { } sheet
					&& RoutineQuestion.From(sheet.RoutineAssignmentId, sheet.RoutineItemId, sheet.RoutineQuestion) == routineQuestion)
				{
					// Free practice of the exercise may have replaced its session meanwhile.
					await _cache.SetStringAsync(
						ExpectedAnswerCacheKey(userId, exercise.ExerciseId),
						JsonConvert.SerializeObject(sheet),
						_expectedAnswerTtl);
					return Content(await SheetMusicResponseAsync(userId, sheet.ExpectedAnswer, routineStatus), "application/json");
				}

				// The teacher's filters are the routine's; the student picks the others.
				foreach (var (group, option) in routine.Item.Filters)
					filters[group] = option;
			}

			var instrument = Request.Cookies["instrument"] ?? "Piano";
			var noteRange = Request.Cookies["noteRange"];

			if (!filters.ContainsKey("instrument"))
				filters["instrument"] = instrument;

			if (!filters.ContainsKey("guitarPosition") && Request.Cookies["guitarPosition"] is { } guitarPosition)
				filters["guitarPosition"] = guitarPosition;

			// With no range, the planner plays the octave the sliders start on.
			if (!filters.ContainsKey("noteRange") && noteRange is not null)
				filters["noteRange"] = noteRange;

			// The round's answer is saved with the exercise filters it was played with.
			var filterJson = ExerciseFilterPresets.ForAnswer(filters, exercise.FiltersJson);

			var plan = _playbackPlanner.Plan(exercise, filters);

			// SolfegeMelody shows its melody as sheet music for the student
			// to sing, so it gets no round: the expected answer is cached for
			// ValidateExercise and the melody is returned in clear text for
			// the staff renderer, with a token for its starting note.
			if (plan.PlaybackPlans.Count == 0)
			{
				// Mixed first, as a round's clips are: a melody whose note can't be played isn't kept.
				var sheetResponse = await SheetMusicResponseAsync(userId, plan.ExpectedAnswerJson, routineStatus);
				var sessionData = new ExerciseSessionData
				{
					ExpectedAnswer = plan.ExpectedAnswerJson,
					Free = request.Free,
					FilterJson = filterJson,
					Timestamp = _clock.GetUtcNow().UtcDateTime,
					RoutineAssignmentId = routineQuestion?.Link.AssignmentId,
					RoutineItemId = routineQuestion?.Link.ItemId,
					RoutineQuestion = routineQuestion?.Number
				};
				await _cache.SetStringAsync(
					ExpectedAnswerCacheKey(userId, request.ExerciseId),
					JsonConvert.SerializeObject(sessionData),
					_expectedAnswerTtl);
				if (routineQuestion is { } sheetQuestion)
					await _routines.RememberQuestionAsync(userId, sheetQuestion.Link, new PendingQuestion(Sheet: sessionData), HttpContext.RequestAborted);
				// Sent verbatim: Json() uses System.Text.Json, which writes every value of a
				// Newtonsoft JObject as an empty array.
				return Content(sheetResponse, "application/json");
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
				filterJson: filterJson,
				routine: routineQuestion,
				cancellationToken: HttpContext.RequestAborted);
			if (routineQuestion is { } roundQuestion)
				await _routines.RememberQuestionAsync(userId, roundQuestion.Link, new PendingQuestion(RoundId: round.RoundId), HttpContext.RequestAborted);

			return Json(PlayResponse(exercise, round, routineStatus));
		}

		private static readonly HashSet<string> StaffExercises = new() {
			"CompleteScale", "CompleteChord", "TransposeScale",
			"MelodicDictation", "RhythmDictation"
		};

		// Uniform response: most exercises ship one play token; only
		// GuessMissingNote ships two (melody1Token, melody2Token).
		// Staff-based exercises also need a `metadata` payload so the
		// front-end can pre-render the prompt notes / staff context
		// without leaking the full answer. A routine question also says
		// where the student stands on the routine item.
		private static Dictionary<string, object?> PlayResponse(Exercise exercise, AudioRound round, RoutineRoundStatus? routine)
		{
			var response = PlayTokens(exercise, round);
			if (routine is not null)
			{
				response["routine"] = routine;
			}
			return response;
		}

		private static Dictionary<string, object?> PlayTokens(Exercise exercise, AudioRound round)
		{
			if (exercise.Name == "GuessMissingNote")
			{
				return new() { ["roundId"] = round.RoundId, ["melody1Token"] = round.Tokens[0], ["melody2Token"] = round.Tokens[1] };
			}

			if (StaffExercises.Contains(exercise.Name))
			{
				// Build a plain CLR dictionary because the action returns via
				// System.Text.Json (no AddNewtonsoftJson is registered) which
				// cannot serialize a Newtonsoft JObject as a real JSON object.
				var expected = JObject.Parse(round.ExpectedAnswerJson);
				var metadata = new Dictionary<string, object?>();
				foreach (var field in new[] {
					"promptNotes", "clef", "keySignature", "timeSignature",
					"numMeasures", "octave", "originalRoot", "targetRoot", "scale", "level",
					"durations", "rests", "slots",
					"root", "firstNote", "firstDuration"
				})
				{
					var token = expected[field];
					if (token != null && token.Type != JTokenType.Null)
					{
						metadata[field] = ToPlainJsonValue(token);
					}
				}
				return new() { ["roundId"] = round.RoundId, ["playToken"] = round.Tokens[0], ["metadata"] = metadata };
			}

			return new() { ["roundId"] = round.RoundId, ["playToken"] = round.Tokens[0] };
		}

		// Sheet music is sent as its expected answer (the melody to sing), with a token for its
		// starting note on the piano, which the page plays only when the student asks for it,
		// and the routine status added to a routine question, as the other exercises have it.
		private async Task<string> SheetMusicResponseAsync(string userId, string expectedAnswerJson, RoutineRoundStatus? routine)
		{
			var startingNote = await _audioMixer.MixAsync(_playbackPlanner.StartingNote(expectedAnswerJson), HttpContext.RequestAborted);
			var response = JObject.Parse(expectedAnswerJson);
			response["startingNoteToken"] = await _audioTokens.IssueTokenAsync(
				userId, $"{startingNote.Container}/{startingNote.BlobName}", HttpContext.RequestAborted);
			if (routine is not null)
			{
				response["routine"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(
					routine, System.Text.Json.JsonSerializerOptions.Web));
			}
			return response.ToString(Formatting.None);
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
			// round also says whether it is a free practice round (the
			// mode is fixed by RequestPlay, never by this request), which
			// filters it was played with and when it was issued.
			string expectedAnswer = null;
			bool roundConsumed = false;
			bool free = false;
			string? filterJson = null;
			DateTimeOffset? issuedAt = null;
			RoutineQuestion? routineQuestion = null;

			if (!string.IsNullOrEmpty(dto.RoundId))
			{
				var round = await _audioTokens.GetRoundAsync(userId, dto.ExerciseId, dto.RoundId, HttpContext.RequestAborted);
				if (round is not null)
				{
					expectedAnswer = round.ExpectedAnswerJson;
					roundConsumed = true;
					free = round.Free;
					filterJson = round.FilterJson;
					issuedAt = round.IssuedAt;
					routineQuestion = round.Routine;
				}
			}

			var legacyKey = ExpectedAnswerCacheKey(userId, dto.ExerciseId);
			if (expectedAnswer is null)
			{
				var json = await _cache.GetStringAsync(legacyKey);
				if (string.IsNullOrEmpty(json))
					return await RoundGoneAsync(userId, dto);
				var legacy = JsonConvert.DeserializeObject<ExerciseSessionData>(json);
				expectedAnswer = legacy.ExpectedAnswer;
				free = legacy.Free;
				filterJson = legacy.FilterJson;
				issuedAt = DateTime.SpecifyKind(legacy.Timestamp, DateTimeKind.Utc);
				routineQuestion = RoutineQuestion.From(legacy.RoutineAssignmentId, legacy.RoutineItemId, legacy.RoutineQuestion);
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
					if (routineQuestion is not null)
						await _routines.RememberAnsweredAsync(userId, dto.ExerciseId, dto.RoundId, routineQuestion.Value, HttpContext.RequestAborted);
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

			var timeZone = UserTimeZone.FromRequest(Request);

			// A routine question's answer takes that question, unless every question is
			// answered already or that one is (in another window, say); should the routine
			// no longer be the student's, it is ordinary practice.
			RoutineRoundContext? routine = null;
			if (routineQuestion is { } asked)
			{
				routine = await _routines.FindAsync(userId, asked.Link, timeZone, HttpContext.RequestAborted);
				if (routine is not null && routine.Item.ExerciseId != exercise.ExerciseId)
					routine = null;
				if (routine is not null && (routine.Item.Progress.IsComplete || asked.Number != routine.Item.NextQuestion))
				{
					await ForgetRoundAsync();
					return AnswerNotTaken(routine.Item);
				}
			}

			var answered = routine is null ? null : routineQuestion;

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

			var answeredAt = _clock.GetUtcNow();
			var now = answeredAt.UtcDateTime;
			// Measured here rather than sent by the client: from Play to this
			// answer, at most five minutes.
			var timeSpentSeconds = AnswerTime.Seconds(issuedAt, answeredAt);

			// Legacy Score row (running totals; kept until the contract step
			// of the score-model split drops the table).
			_context.Scores.Add(new Score
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				CorrectCount = correctCount,
				ErrorCount = errorCount,
				BestScore = bestScore,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = now
			});

			// New per-attempt snapshot.
			_context.ScoreSnapshots.Add(new ScoreSnapshot
			{
				UserId = userId,
				ExerciseId = exercise.ExerciseId,
				IsCorrect = isCorrect,
				TimeSpentSeconds = timeSpentSeconds,
				Timestamp = now,
				FilterJson = filterJson,
				RoutineAssignmentId = answered?.Link.AssignmentId,
				RoutineItemId = answered?.Link.ItemId,
				RoutineQuestion = answered?.Number
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

			try
			{
				await _context.SaveChangesAsync();
			}
			catch (DbUpdateException) when (answered is { } question)
			{
				// Two windows answered the same question at once: the unique index on the
				// routine columns refused this answer, and nothing of it was saved.
				_context.ChangeTracker.Clear();
				var taken = await _context.ScoreSnapshots.AnyAsync(s =>
					s.UserId == userId && s.RoutineAssignmentId == question.Link.AssignmentId &&
					s.RoutineItemId == question.Link.ItemId && s.RoutineQuestion == question.Number);
				if (!taken)
					throw;

				await ForgetRoundAsync();
				var current = await _routines.FindAsync(userId, question.Link, timeZone, HttpContext.RequestAborted);
				return AlreadyAnswered(current is null
					? RoutineRoundStatus.Unavailable(_localizer)
					: RoutineRoundStatus.From(current.Item, _localizer));
			}

			await ForgetRoundAsync();
			
			await _analyticsService.SaveAttemptAsync(new ExerciseAttemptLog
			{
				UserId = userId,
				Exercise = exercise.Name,
				Timestamp = now,
				QuestionId = currentAnswer,
				Attempt = new AttemptDetails
				{
					UserAnswer = dto.UserGuess,
					ExpectedAnswer = currentAnswer,
					IsCorrect = isCorrect,
					TimeSpentSeconds = timeSpentSeconds,
				}
			});

			// XP, level and badges are a bonus: a failure here must not lose the
			// answer that was already scored above.
			object? rewards = null;
			try
			{
				var attempt = await _gamification.RecordAttemptAsync(
					userId, isCorrect, timeZone, HttpContext.RequestAborted);
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
				path,
				routine = routine is null ? null : RoutineRoundStatus.From(routine.Item.Answered(isCorrect), _localizer)
			});
		}

		/// <summary>
		/// Answers a round that is no longer there. When it asked a routine question that was
		/// answered since (in another window showing the same question, say), the student is
		/// told so and where the routine stands; otherwise the round expired.
		/// </summary>
		private async Task<JsonResult> RoundGoneAsync(string userId, ValidateExerciseDto dto)
		{
			if (!string.IsNullOrEmpty(dto.RoundId)
				&& await _routines.AnsweredQuestionAsync(userId, dto.ExerciseId, dto.RoundId, HttpContext.RequestAborted) is { } asked)
			{
				var routine = await _routines.FindAsync(userId, asked.Link, UserTimeZone.FromRequest(Request), HttpContext.RequestAborted);
				if (routine is not null && routine.Item.ExerciseId == dto.ExerciseId)
					return AnswerNotTaken(routine.Item);
			}

			return Json(new { success = false, message = _localizer["Exercise.SessionExpired"].Value, isCorrect = false });
		}

		/// <summary>Refuses an answer to a routine question that takes none: every question is answered, or that one is.</summary>
		private JsonResult AnswerNotTaken(AssignedRoutineItem item)
		{
			var status = RoutineRoundStatus.From(item, _localizer);
			return item.Progress.IsComplete
				? Json(new { success = false, isCorrect = false, message = status.Blocked!.Message, routine = status })
				: AlreadyAnswered(status);
		}

		private JsonResult AlreadyAnswered(RoutineRoundStatus status) => Json(new
		{
			success = false,
			isCorrect = false,
			title = _localizer["Routine.AlreadyAnsweredTitle"].Value,
			message = _localizer["Routine.AlreadyAnswered"].Value,
			routine = status
		});

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
