namespace AcademiaAuditiva.Models.Teaching;

/// <summary>
/// A student a class assignment goes to when the teacher ticked some of the class
/// (<see cref="RoutineAssignment.ChosenStudentsOnly"/>). The row stays when the student leaves
/// the class, but the routine only shows while they are a member.
/// </summary>
public class RoutineAssignmentStudent
{
    public int RoutineAssignmentId { get; set; }
    public RoutineAssignment? RoutineAssignment { get; set; }

    public string StudentId { get; set; } = string.Empty;
    public ApplicationUser? Student { get; set; }
}
