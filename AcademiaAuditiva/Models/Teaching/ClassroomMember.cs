namespace AcademiaAuditiva.Models.Teaching;

public class ClassroomMember
{
    public int Id { get; set; }

    public int ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public string StudentId { get; set; } = string.Empty;
    public ApplicationUser? Student { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
