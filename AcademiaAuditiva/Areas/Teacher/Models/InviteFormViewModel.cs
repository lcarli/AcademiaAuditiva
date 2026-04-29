using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Areas.Teacher.Models;

public class InviteFormViewModel
{
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Student email")]
    public string Email { get; set; } = string.Empty;

    public int ClassroomId { get; set; }
}
