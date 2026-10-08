using System.Security.Claims;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Games;
using AcademiaAuditiva.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Controllers;

/// <summary>
/// Game modes: the hub that starts them, and the endpoints the exercise page's game bar
/// (wwwroot/js/core/game.js) calls. The rounds themselves are asked and answered by
/// ExerciseController like any other, tagged with the run.
/// </summary>
[Authorize]
public class GamesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly GameService _games;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly TimeProvider _clock;

    public GamesController(ApplicationDbContext db, GameService games, IStringLocalizer<SharedResources> localizer, TimeProvider clock)
    {
        _db = db;
        _games = games;
        _localizer = localizer;
        _clock = clock;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private CancellationToken Aborted => HttpContext.RequestAborted;

    public async Task<IActionResult> Index()
    {
        var userId = UserId;
        var exercises = ExerciseCatalog.ByCategory
            .Select(c => new CatalogCategory(c.Name, [.. c.Exercises.Where(e => GameModes.Playable(e.Name))]))
            .Where(c => c.Exercises.Count > 0)
            .ToList();

        var latest = await _games.LatestPlacementAsync(userId, Aborted);
        var continueUrl = latest is null ? null : await ContinueUrlAsync(latest);
        var placement = new PlacementCardViewModel(await _games.PlacementAvailableAsync(Aborted), latest, continueUrl);

        return View(new GamesHubViewModel(
            exercises,
            await _games.RecordsAsync(userId, Aborted),
            await _games.WeakSpotsAsync(userId, Aborted),
            placement));
    }

    /// <summary>The hub's form: opens the exercise page in the mode.</summary>
    [HttpGet]
    public IActionResult Play(string? mode, string? exercise)
    {
        var parsed = GameModes.Parse(mode);
        if (!GameModes.IsExerciseMode(parsed)
            || ExerciseCatalog.All.FirstOrDefault(e => e.Name == exercise) is not { } entry
            || !GameModes.Playable(entry.Name))
            return RedirectToAction(nameof(Index));

        return Redirect(Url.GameUrl(entry.Name, parsed!));
    }

    public sealed record StartRequest(string? Mode, int ExerciseId, Dictionary<string, string>? Filters);

    /// <summary>Starts a run with the settings of the page's first Play.</summary>
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartRequest request)
    {
        var mode = GameModes.Parse(request?.Mode);
        if (request is null || !GameModes.IsExerciseMode(mode))
            return BadRequest();

        var exercise = await _db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.ExerciseId == request.ExerciseId, Aborted);
        if (exercise is null || !GameModes.Playable(exercise.Name))
            return BadRequest();

        var now = Now;
        var filterJson = ExerciseFilterPresets.ForAnswer(request.Filters, exercise.FiltersJson);
        var run = await _games.StartAsync(UserId, mode!, exercise, filterJson, now, Aborted);
        var progress = await _games.ProgressAsync(run, now, recount: false, Aborted);
        return Json(new { success = true, game = GameDisplay.Status(_localizer, progress, now) });
    }

    public sealed record FinishRequest(int RunId);

    /// <summary>Ends a run: the sprint's time is up, or the player stopped.</summary>
    [HttpPost]
    public async Task<IActionResult> Finish([FromBody] FinishRequest request)
    {
        var run = request is null ? null : await _games.FindAsync(UserId, request.RunId, Aborted);
        if (run is null || run.Mode == GameModes.Placement)
            return BadRequest();

        var now = Now;
        var progress = await _games.FinishAsync(run, now, Aborted);
        return Json(new { success = true, game = GameDisplay.Status(_localizer, progress, now) });
    }

    /// <summary>Starts a placement test (ending an unfinished one) on its first exercise.</summary>
    [HttpPost]
    public async Task<IActionResult> StartPlacement()
    {
        if (!await _games.PlacementAvailableAsync(Aborted))
        {
            TempData["Error"] = _localizer["Toast.PlacementUnavailable"].Value;
            return RedirectToAction(nameof(Index));
        }

        var run = await _games.StartPlacementAsync(UserId, Now, Aborted);
        return Redirect(await ContinueUrlAsync(run) ?? Url.Action(nameof(Placement), new { id = run.Id })!);
    }

    /// <summary>A placement test's result, or where it stands.</summary>
    [HttpGet]
    public async Task<IActionResult> Placement(int id)
    {
        var run = await _games.FindAsync(UserId, id, Aborted);
        if (run is null || run.Mode != GameModes.Placement)
            return NotFound();

        var progress = await _games.ProgressAsync(run, Now, recount: false, Aborted);
        return View(new PlacementResultViewModel(run, progress.Placement!, await ContinueUrlAsync(run, progress)));
    }

    /// <summary>Skips the learning path to the unit the test suggested.</summary>
    [HttpPost]
    public async Task<IActionResult> ApplyPlacement(int id)
    {
        var run = await _games.FindAsync(UserId, id, Aborted);
        if (run is null || run.Mode != GameModes.Placement)
            return NotFound();

        if (!await _games.ApplyPlacementAsync(run, Now, Aborted))
            return RedirectToAction(nameof(Placement), new { id });

        TempData["Success"] = _localizer["Toast.PlacementApplied"].Value;
        return RedirectToAction("Index", "LearningPath");
    }

    /// <summary>The page of the test's current exercise, while it is unfinished and open.</summary>
    private async Task<string?> ContinueUrlAsync(Models.GameRun run, GameProgress? progress = null)
    {
        if (run.Mode != GameModes.Placement) return null;
        progress ??= await _games.ProgressAsync(run, Now, recount: false, Aborted);
        if (progress.Over || progress.Placement?.Current is not { } item) return null;

        var link = await _games.ItemLinkAsync(item, Aborted);
        return link is null ? null : Url.GameUrl(item.Exercise, GameModes.Placement, link.Filters, run.Id);
    }
}
