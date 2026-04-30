namespace AcademiaAuditiva.Models;
public class ValidateExerciseDto
{
    public int ExerciseId { get; set; }
    public string UserGuess { get; set; }
    public int TimeSpentSeconds { get; set; }

    /// <summary>
    /// Identifier of the round previously issued by <c>RequestPlay</c>.
    /// The server uses it to look up the expected answer that was
    /// generated for this exact play, so duplo-clicks and rapid replays
    /// can't validate against a fresher answer than the one the user
    /// actually heard.
    /// </summary>
    public string RoundId { get; set; }
}


