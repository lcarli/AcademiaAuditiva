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
}