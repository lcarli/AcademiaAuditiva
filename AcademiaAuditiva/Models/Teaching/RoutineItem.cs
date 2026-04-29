namespace AcademiaAuditiva.Models.Teaching;

public class RoutineItem
{
    public int Id { get; set; }

    public int RoutineId { get; set; }
    public Routine? Routine { get; set; }

    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    public int Order { get; set; }

    /// <summary>Optional JSON of filters (matches Exercise.FiltersJson shape).</summary>
    public string? FilterJson { get; set; }

    public int TargetCount { get; set; } = 10;
    public int? MinScore { get; set; }
}
