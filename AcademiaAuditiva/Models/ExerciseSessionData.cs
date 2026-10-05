public class ExerciseSessionData
{
    public string ExpectedAnswer { get; set; }

    /// <summary>Free practice round (see <c>PlayRequestDto.Free</c>).</summary>
    public bool Free { get; set; }

    /// <summary>The exercise filters the round was played with (see <c>AudioRound.FilterJson</c>).</summary>
    public string? FilterJson { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}