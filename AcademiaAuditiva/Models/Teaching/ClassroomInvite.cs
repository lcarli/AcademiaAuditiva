using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models.Teaching;

public class ClassroomInvite
{
    public int Id { get; set; }

    public int ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
}
