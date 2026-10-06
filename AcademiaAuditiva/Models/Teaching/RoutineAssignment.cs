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

    /// <summary>The due date (no time): the routine is due by the end of that day in the student's time zone.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>Whether answers are still accepted after the due date; otherwise the routine closes then.</summary>
    public bool AllowLate { get; set; }

    public ICollection<RoutineAssignmentOverride> Overrides { get; set; } = new List<RoutineAssignmentOverride>();
}
