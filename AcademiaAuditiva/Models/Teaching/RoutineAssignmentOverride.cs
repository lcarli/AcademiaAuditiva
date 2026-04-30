namespace AcademiaAuditiva.Models.Teaching;

/// <summary>
/// Per-student override of a single item in a classroom-wide routine assignment.
/// </summary>
public class RoutineAssignmentOverride
{
    public int Id { get; set; }

    public int RoutineAssignmentId { get; set; }
    public RoutineAssignment? RoutineAssignment { get; set; }

    public string StudentId { get; set; } = string.Empty;
    public ApplicationUser? Student { get; set; }

    public int RoutineItemId { get; set; }
    public RoutineItem? RoutineItem { get; set; }

    public string? OverrideFilterJson { get; set; }
    public int? OverrideTargetCount { get; set; }
    public bool ExcludeItem { get; set; }
}
