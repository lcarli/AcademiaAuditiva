using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class DifficultyLevel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } // Beginner, Intermediate, Advanced

    public string DisplayName { get; set; }
}
