namespace AcademiaAuditiva.Models.Teaching;

/// <summary>
/// A routine assigned to a classroom: to the whole class, or to the students ticked in it
/// (<see cref="ChosenStudentsOnly"/>). Older assignments went to a single student instead
/// (<see cref="StudentId"/>); exactly one of <see cref="ClassroomId"/> or <see cref="StudentId"/> is set.
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

    /// <summary>
    /// True when the routine goes only to the class members in <see cref="ChosenStudents"/>, even
    /// once none of them is left; false when it goes to the whole class, newcomers included.
    /// </summary>
    public bool ChosenStudentsOnly { get; set; }

    public ICollection<RoutineAssignmentStudent> ChosenStudents { get; set; } = new List<RoutineAssignmentStudent>();

    public ICollection<RoutineAssignmentOverride> Overrides { get; set; } = new List<RoutineAssignmentOverride>();
}
