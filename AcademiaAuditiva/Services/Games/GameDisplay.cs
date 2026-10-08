using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.Games;

/// <summary>
/// A run as the exercise page's game bar (wwwroot/js/core/game.js) shows it, every text
/// localized: RequestPlay, ValidateExercise and the Games endpoints return it as <c>game</c>.
/// </summary>
/// <param name="Total">Questions in the run, when it has a set number.</param>
/// <param name="SecondsLeft">The sprint's time left.</param>
/// <param name="Text">The bar's score line.</param>
/// <param name="NextUrl">The placement test's next exercise, when it is on another page.</param>
/// <param name="ResultUrl">The placement test's result, once it is finished.</param>
/// <param name="End">The game over dialog, once the run is over.</param>
public sealed record GameStatus(
    int RunId,
    string Mode,
    int Score,
    int Answered,
    int? Total,
    bool Over,
    int? SecondsLeft,
    string Text,
    string? NextUrl,
    string? ResultUrl,
    GameEnd? End);

/// <param name="Record">The player's best score, or that this run beat it.</param>
public sealed record GameEnd(string Title, string Text, string? Record, bool NewBest);

public static class GameDisplay
{
    /// <summary>The resource key part of a mode: Game.{Key}.Title and the like.</summary>
    public static string Key(string mode) => mode switch
    {
        GameModes.Sprint => "Sprint",
        GameModes.Survival => "Survival",
        GameModes.WeakSpots => "WeakSpots",
        GameModes.Placement => "Placement",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string Icon(string mode) => mode switch
    {
        GameModes.Sprint => "bi-stopwatch",
        GameModes.Survival => "bi-heart-pulse",
        GameModes.WeakSpots => "bi-bullseye",
        _ => "bi-speedometer2",
    };

    /// <summary>The exercise page in a game mode, with its settings in the query string like learning path links.</summary>
    public static string GameUrl(this IUrlHelper url, string exercise, string mode,
        IReadOnlyDictionary<string, string>? filters = null, int? runId = null)
    {
        var values = new RouteValueDictionary(
            (filters ?? new Dictionary<string, string>()).ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
        {
            ["area"] = string.Empty,
            ["game"] = mode,
        };
        if (runId is not null) values["run"] = runId;
        return url.Action(exercise, "Exercise", values) ?? "/Exercise/" + exercise;
    }

    public static string ExerciseTitle(this IStringLocalizer localizer, string exercise)
    {
        var text = localizer[exercise];
        return text.ResourceNotFound ? exercise : text.Value;
    }

    /// <summary>The bar's score line.</summary>
    public static string ScoreText(this IStringLocalizer localizer, GameRun run, PlacementState? placement, bool over)
        => run.Mode switch
        {
            GameModes.Sprint => localizer["Game.Bar.Correct", run.Score],
            GameModes.Survival => localizer["Game.Bar.Streak", run.Score],
            GameModes.WeakSpots => over
                ? localizer["Game.Bar.Done", run.Score, GameRules.WeakSpotQuestions]
                : localizer["Game.Bar.Question", Math.Min(run.Answered + 1, GameRules.WeakSpotQuestions), GameRules.WeakSpotQuestions, run.Score],
            _ => placement?.Current is { } item && !over
                ? localizer["Game.Bar.Placement", placement.ItemIndex + 1, PlacementTest.Items.Count, placement.Question, PlacementTest.QuestionsPerItem, localizer.ExerciseTitle(item.Exercise)]
                : localizer["Game.Bar.PlacementDone"],
        };

    public static GameStatus Status(IStringLocalizer localizer, GameProgress progress, DateTime now,
        string? nextUrl = null, string? resultUrl = null)
    {
        var run = progress.Run;
        GameEnd? end = null;
        if (progress.Over)
        {
            var key = Key(run.Mode);
            var text = run.Mode switch
            {
                GameModes.Sprint => localizer["Game.Sprint.EndText", run.Score, run.Answered],
                GameModes.Survival => localizer["Game.Survival.EndText", run.Score],
                GameModes.WeakSpots => localizer["Game.WeakSpots.EndText", run.Score, run.Answered],
                _ => localizer["Game.Placement.EndText"],
            };
            string? record = progress.NewBest ? localizer["Game.End.NewBest"]
                : progress.Best is { } best && GameModes.RecordModes.Contains(run.Mode) ? localizer["Game.End.Best", Math.Max(best, run.Score)]
                : null;
            end = new GameEnd(localizer[$"Game.{key}.EndTitle"], text, record, progress.NewBest);
        }

        return new GameStatus(
            run.Id,
            run.Mode,
            run.Score,
            run.Answered,
            run.Mode == GameModes.WeakSpots ? GameRules.WeakSpotQuestions : null,
            progress.Over,
            GameRules.SecondsLeft(run, now),
            localizer.ScoreText(run, progress.Placement, progress.Over),
            progress.Over ? null : nextUrl,
            resultUrl,
            end);
    }
}
