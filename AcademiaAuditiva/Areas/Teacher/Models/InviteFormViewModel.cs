using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Areas.Teacher.Models;

public class InviteFormViewModel
{
    [Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.EmailAddress"), StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Teacher.Members.StudentEmail")]
    public string Email { get; set; } = string.Empty;

    public int ClassroomId { get; set; }
}
