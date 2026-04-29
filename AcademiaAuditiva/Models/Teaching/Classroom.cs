using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models.Teaching;

public class Classroom
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public ApplicationUser? Owner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsArchived { get; set; }

    public ICollection<ClassroomMember> Members { get; set; } = new List<ClassroomMember>();
    public ICollection<ClassroomInvite> Invites { get; set; } = new List<ClassroomInvite>();
}
