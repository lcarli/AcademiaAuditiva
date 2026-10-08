using AcademiaAuditiva.Models.Teaching;

namespace AcademiaAuditiva.Models;

/// <summary>What a <see cref="Notification"/> tells its reader. Stored as numbers, so never renumber them.</summary>
public enum NotificationKind
{
    /// <summary>A teacher assigned the reader a routine.</summary>
    RoutineAssigned = 1,

    /// <summary>A routine of the reader's that they haven't finished is due the next day.</summary>
    RoutineDueTomorrow = 2,

    /// <summary>A student answered every question of a routine the reader assigned.</summary>
    StudentFinishedRoutine = 3,

    /// <summary>A student accepted the reader's invitation and joined their classroom.</summary>
    InviteAccepted = 4,
}

/// <summary>
/// A notification shown on the site to one user, under the bell in the header. It is about a
/// student: the reader themself in a student's notifications, one of their students in a teacher's.
/// It goes with the assignment or classroom it is about, and after <see cref="Retention"/>.
/// </summary>
public class Notification
{
    /// <summary>How long notifications are kept, read or not.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public int Id { get; set; }

    /// <summary>The user who reads it.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public NotificationKind Kind { get; set; }

    /// <summary>The student it is about: the reader themself, unless the reader is their teacher.</summary>
    public string StudentId { get; set; } = string.Empty;
    public ApplicationUser? Student { get; set; }

    /// <summary>The assignment it is about; null only for <see cref="NotificationKind.InviteAccepted"/>.</summary>
    public int? RoutineAssignmentId { get; set; }
    public RoutineAssignment? RoutineAssignment { get; set; }

    /// <summary>The classroom joined, for <see cref="NotificationKind.InviteAccepted"/>.</summary>
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the reader opened it or marked them all read; null while unread.</summary>
    public DateTime? ReadAt { get; set; }
}
