using System.Security.Claims;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>
/// The routine question an exercise page is open for. My Training links to the exercise with
/// the routine in the query string (<see cref="RoutineLink.FromQuery"/>); the page's routine
/// banner (Views/Exercise/_RoutineBanner) and its filters, which the routine sets, both ask
/// for it, so it is looked up once per request.
/// </summary>
public sealed class RoutinePage
{
    private readonly RoutineRounds _rounds;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private (int ExerciseId, Task<RoutinePageState?> State)? _resolved;

    public RoutinePage(RoutineRounds rounds, IStringLocalizer<SharedResources> localizer)
    {
        _rounds = rounds;
        _localizer = localizer;
    }

    /// <summary>Null unless the page is open for a routine question.</summary>
    public Task<RoutinePageState?> ResolveAsync(HttpContext context, int exerciseId)
    {
        if (_resolved is { } resolved && resolved.ExerciseId == exerciseId)
            return resolved.State;

        var state = LoadAsync(context, exerciseId);
        _resolved = (exerciseId, state);
        return state;
    }

    private async Task<RoutinePageState?> LoadAsync(HttpContext context, int exerciseId)
    {
        if (RoutineLink.FromQuery(context.Request.Query) is not { } link)
            return null;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var round = string.IsNullOrEmpty(userId)
            ? null
            : await _rounds.FindAsync(userId, link, UserTimeZone.FromRequest(context.Request), context.RequestAborted);
        return round is null || round.Item.ExerciseId != exerciseId
            ? new RoutinePageState(link, null, RoutineRoundStatus.Unavailable(_localizer))
            : new RoutinePageState(link, round, RoutineRoundStatus.From(round.Item, _localizer));
    }
}

/// <param name="Round">Null when the routine item is not the student's, or is of another exercise.</param>
public sealed record RoutinePageState(RoutineLink Link, RoutineRoundContext? Round, RoutineRoundStatus Status);
