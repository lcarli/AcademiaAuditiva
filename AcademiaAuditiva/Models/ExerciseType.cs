using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class ExerciseType
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; }

    public string DisplayName { get; set; }
}
