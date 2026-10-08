using System.Globalization;
using System.Security.Claims;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Routines;

namespace AcademiaAuditiva.Services.Games;

/// <summary>
/// The game an exercise page is open for: <c>?game=sprint</c> and the other exercise modes,
/// or <c>?game=placement&amp;run=12</c> for a placement test's exercise. The game bar
/// (Views/Exercise/_GameBar) and the filters, which a placement test sets, both ask for it,
/// so it is looked up once per request. A routine question takes the page over instead.
/// </summary>
public sealed class GamePage
{
    private readonly GameService _games;
    private readonly TimeProvider _clock;
    private (int ExerciseId, Task<GamePageState?> State)? _resolved;

    public GamePage(GameService games, TimeProvider clock)
    {
        _games = games;
        _clock = clock;
    }

    /// <summary>Null unless the page is open for a game it can be played in.</summary>
    /// <param name="exerciseName">The exercise's name, looked up when not given.</param>
    public Task<GamePageState?> ResolveAsync(HttpContext context, int exerciseId, string? exerciseName = null)
    {
        if (_resolved is { } resolved && resolved.ExerciseId == exerciseId)
            return resolved.State;

        var state = LoadAsync(context, exerciseId, exerciseName);
        _resolved = (exerciseId, state);
        return state;
    }

    private async Task<GamePageState?> LoadAsync(HttpContext context, int exerciseId, string? exerciseName)
    {
        var query = context.Request.Query;
        if (GameModes.Parse(query["game"]) is not { } mode || RoutineLink.FromQuery(query) is not null)
            return null;
        exerciseName ??= await _games.ExerciseNameAsync(exerciseId, context.RequestAborted);
        if (exerciseName is null || !GameModes.Playable(exerciseName))
            return null;

        if (mode != GameModes.Placement)
            return new GamePageState(mode, null, null, null);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        GameRun? run = null;
        if (!string.IsNullOrEmpty(userId)
            && int.TryParse(query["run"], NumberStyles.None, CultureInfo.InvariantCulture, out var runId))
        {
            run = await _games.FindAsync(userId, runId, context.RequestAborted);
        }
        if (run is null || run.Mode != GameModes.Placement)
            return new GamePageState(mode, null, null, null);

        var progress = await _games.ProgressAsync(run, _clock.GetUtcNow().UtcDateTime, recount: false, context.RequestAborted);
        if (progress.Over || progress.Placement?.Current is not { } item || item.Exercise != exerciseName)
            return new GamePageState(mode, progress, null, null);

        var link = await _games.ItemLinkAsync(item, context.RequestAborted);
        return link is null || link.ExerciseId != exerciseId
            ? new GamePageState(mode, progress, null, null)
            : new GamePageState(mode, progress, link, link.Filters);
    }
}

/// <param name="Progress">The placement test the page belongs to; null for the other modes.</param>
/// <param name="Item">The placement test's exercise this page asks, when the test is on it.</param>
/// <param name="LockedFilters">Settings the game sets (the placement item's preset).</param>
public sealed record GamePageState(
    string Mode,
    GameProgress? Progress,
    PlacementItemLink? Item,
    IReadOnlyDictionary<string, string>? LockedFilters)
{
    /// <summary>A placement link that no longer fits the page: the test is over, or on another exercise.</summary>
    public bool Stale => Mode == GameModes.Placement && Item is null;
}
