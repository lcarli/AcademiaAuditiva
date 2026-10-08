using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Games;

namespace AcademiaAuditiva.ViewModels;

/// <summary>The games hub (Views/Games/Index).</summary>
/// <param name="Exercises">The exercises a game can be played on, by category.</param>
public sealed record GamesHubViewModel(
    IReadOnlyList<CatalogCategory> Exercises,
    IReadOnlyList<GameRecord> Records,
    IReadOnlyList<WeakSpotLink> WeakSpots,
    PlacementCardViewModel Placement);

/// <summary>The hub's placement test card.</summary>
/// <param name="Available">Whether every exercise of the test is seeded.</param>
/// <param name="Latest">The player's latest test, if any.</param>
/// <param name="ContinueUrl">Its current exercise, while it is unfinished and open.</param>
public sealed record PlacementCardViewModel(bool Available, GameRun? Latest, string? ContinueUrl)
{
    public bool InProgress => ContinueUrl is not null;

    public bool Finished => Latest?.PlacementUnit is not null;
}

/// <summary>A placement test's result page (Views/Games/Placement).</summary>
/// <param name="ContinueUrl">Its current exercise, while it is unfinished and open.</param>
public sealed record PlacementResultViewModel(GameRun Run, PlacementState State, string? ContinueUrl)
{
    public bool Finished => Run.PlacementUnit is not null;

    public bool CanApply => Run.PlacementUnit is > 1 && Run.AppliedAt is null;
}
