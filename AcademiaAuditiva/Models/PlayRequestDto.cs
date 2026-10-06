namespace AcademiaAuditiva.Models;

public class PlayRequestDto
{
    public int ExerciseId { get; set; }
    public Dictionary<string, string> Filters { get; set; }

    /// <summary>
    /// Free practice: the round is checked but never scored, and its answer
    /// can be revealed before answering. The mode stays with the round, so
    /// ValidateExercise never takes it from the client.
    /// </summary>
    public bool Free { get; set; }

    /// <summary>
    /// The routine item to ask a question of (both or neither): the routine assignment and
    /// its item, from the exercise page's query string. Never with <see cref="Free"/>.
    /// </summary>
    public int? RoutineAssignmentId { get; set; }

    /// <inheritdoc cref="RoutineAssignmentId"/>
    public int? RoutineItemId { get; set; }
}