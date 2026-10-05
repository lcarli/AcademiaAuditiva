namespace AcademiaAuditiva.Services.Scoring;

/// <summary>
/// The time saved with an answer (<c>TimeSpentSeconds</c>): from when
/// <c>RequestPlay</c> issued the round to when <c>ValidateExercise</c>
/// checked the answer. The server measures it, so the client cannot
/// inflate it, and a round left open counts at most
/// <see cref="MaxSeconds"/>.
/// </summary>
public static class AnswerTime
{
    /// <summary>Five minutes: the most one answer adds to the practice time.</summary>
    public const int MaxSeconds = 300;

    /// <summary>
    /// Whole seconds from <paramref name="issuedAt"/> to <paramref name="answeredAt"/>,
    /// between 0 and <see cref="MaxSeconds"/>. A round without an issue time (cached
    /// before rounds recorded one) counts 0.
    /// </summary>
    public static int Seconds(DateTimeOffset? issuedAt, DateTimeOffset answeredAt)
    {
        if (issuedAt is null)
        {
            return 0;
        }

        // Replicas' clocks can differ slightly, hence the floor at 0.
        var seconds = Math.Clamp((answeredAt - issuedAt.Value).TotalSeconds, 0, MaxSeconds);
        return (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
    }
}
