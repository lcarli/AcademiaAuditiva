using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Services.Games;

/// <summary>The game modes, stored in <see cref="GameRun.Mode"/> and used in the <c>?game=</c> query.</summary>
public static class GameModes
{
    /// <summary>As many right answers as possible in <see cref="GameRules.SprintLength"/>.</summary>
    public const string Sprint = "sprint";

    /// <summary>Right answers in a row: the first wrong one ends the run.</summary>
    public const string Survival = "survival";

    /// <summary><see cref="GameRules.WeakSpotQuestions"/> questions on an exercise and settings the player often gets wrong.</summary>
    public const string WeakSpots = "weakspots";

    /// <summary>A few questions per learning path unit, which suggests where to start on the path.</summary>
    public const string Placement = "placement";

    /// <summary>The modes played on one exercise the player picks (or the hub suggests).</summary>
    public static IReadOnlyList<string> ExerciseModes { get; } = [Sprint, Survival, WeakSpots];

    /// <summary>The modes whose best score the hub shows.</summary>
    public static IReadOnlyList<string> RecordModes { get; } = [Sprint, Survival];

    public static bool IsExerciseMode(string? mode) => mode is not null && ExerciseModes.Contains(mode);

    public static bool IsKnown(string? mode) => mode == Placement || IsExerciseMode(mode);

    /// <summary>The query value as a mode, or null: <c>?game=Sprint</c> works too.</summary>
    public static string? Parse(string? value)
    {
        var mode = value?.Trim().ToLowerInvariant();
        return IsKnown(mode) ? mode : null;
    }

    /// <summary>Exercises that can't be played as a game: the sung ones need a microphone.</summary>
    public static bool Playable(string exerciseName) => !MicrophoneExercises.Contains(exerciseName);
}
