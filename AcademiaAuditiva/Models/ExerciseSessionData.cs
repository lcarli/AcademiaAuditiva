public class ExerciseSessionData
{
    public string ExpectedAnswer { get; set; }

    /// <summary>Free practice round (see <c>PlayRequestDto.Free</c>).</summary>
    public bool Free { get; set; }

    /// <summary>The exercise filters the round was played with (see <c>AudioRound.FilterJson</c>).</summary>
    public string? FilterJson { get; set; }

    /// <summary>When <c>RequestPlay</c> issued the round (UTC); the answer's time is measured from it.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>The routine question the round asks (see <c>AudioRound.Routine</c>); null outside routines.</summary>
    public int? RoutineAssignmentId { get; set; }

    /// <inheritdoc cref="RoutineAssignmentId"/>
    public int? RoutineItemId { get; set; }

    /// <inheritdoc cref="RoutineAssignmentId"/>
    public int? RoutineQuestion { get; set; }
}