namespace AcademiaAuditiva.Models.Teaching;

/// <summary>
/// A routine assigned to either a whole classroom or a single student.
/// Exactly one of <see cref="ClassroomId"/> or <see cref="StudentId"/> must be set.
/// </summary>
public class RoutineAssignment
{
    public int Id { get; set; }

    public int RoutineId { get; set; }
    public Routine? Routine { get; set; }

    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public string? StudentId { get; set; }
    public ApplicationUser? Student { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueAt { get; set; }

    public ICollection<RoutineAssignmentOverride> Overrides { get; set; } = new List<RoutineAssignmentOverride>();
}
