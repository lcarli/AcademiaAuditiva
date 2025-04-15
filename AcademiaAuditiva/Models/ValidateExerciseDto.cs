namespace AcademiaAuditiva.Models;
public class ValidateExerciseDto
{
    public int ExerciseId { get; set; }
    public string UserGuess { get; set; }
    public string ActualAnswer { get; set; }
    public int TimeSpentSeconds { get; set; }
}

