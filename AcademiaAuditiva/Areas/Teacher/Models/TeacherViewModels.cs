using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Areas.Teacher.Models;

public class TeacherHomeViewModel
{
    public int ActiveClassrooms { get; set; }
    public int Routines { get; set; }
    public int PendingInvites { get; set; }
}

public class ClassroomFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(120, MinimumLength = 2)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}

public class ClassroomListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public int PendingInvites { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}
