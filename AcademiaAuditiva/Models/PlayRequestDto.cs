namespace AcademiaAuditiva.Models;

public class PlayRequestDto
{
    public int ExerciseId { get; set; }
    public Dictionary<string, string> Filters { get; set; }
}