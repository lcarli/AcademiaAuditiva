using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Services.Games;

/// <summary>When a game run takes rounds and answers, and when it is over.</summary>
public static class GameRules
{
    public static readonly TimeSpan SprintLength = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How late a sprint answer may reach the server and still count: a round asked before the
    /// whistle is answered on the way.
    /// </summary>
    public static readonly TimeSpan SprintGrace = TimeSpan.FromSeconds(3);

    public const int WeakSpotQuestions = 10;

    /// <summary>How long an unfinished run stays open; the placement test may be taken over a day.</summary>
    public static readonly TimeSpan RunLifetime = TimeSpan.FromHours(2);

    public static readonly TimeSpan PlacementLifetime = TimeSpan.FromHours(24);

    /// <summary>When the run stops taking rounds, whatever happens in it.</summary>
    public static DateTime Deadline(GameRun run) => run.Mode switch
    {
        GameModes.Sprint => run.StartedAt + SprintLength,
        GameModes.Placement => run.StartedAt + PlacementLifetime,
        _ => run.StartedAt + RunLifetime,
    };

    /// <summary>Whether the run takes a new round now.</summary>
    public static bool IsOpen(GameRun run, DateTime now) => run.EndedAt is null && now < Deadline(run);

    /// <summary>
    /// Whether an answer given at <paramref name="answeredAt"/> counts for the run. A sprint
    /// takes answers up to <see cref="SprintGrace"/> after it ends (on time or when the player
    /// stops it); the other modes only while they are open.
    /// </summary>
    public static bool TakesAnswer(GameRun run, DateTime answeredAt)
    {
        if (run.Mode != GameModes.Sprint) return IsOpen(run, answeredAt);

        var end = Deadline(run);
        if (run.EndedAt is { } ended && ended < end) end = ended;
        return answeredAt <= end + SprintGrace;
    }

    /// <summary>
    /// Whether the run is over after <paramref name="score"/> right answers out of
    /// <paramref name="answered"/>, at <paramref name="now"/>. The placement test ends when
    /// <see cref="PlacementTest"/> says so, or when it expires.
    /// </summary>
    public static bool IsOver(GameRun run, int score, int answered, DateTime now) => run.EndedAt is not null || run.Mode switch
    {
        GameModes.Survival => answered > score || now >= Deadline(run),
        GameModes.WeakSpots => answered >= WeakSpotQuestions || now >= Deadline(run),
        _ => now >= Deadline(run),
    };

    /// <summary>Whole seconds the sprint has left (rounded up), or null for the other modes.</summary>
    public static int? SecondsLeft(GameRun run, DateTime now)
    {
        if (run.Mode != GameModes.Sprint) return null;
        if (run.EndedAt is not null) return 0;
        var left = Deadline(run) - now;
        return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
    }
}
