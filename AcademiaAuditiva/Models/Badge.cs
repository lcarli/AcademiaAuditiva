using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class Badge
{
    [Key]
    [MaxLength(50)]
    public string BadgeKey { get; set; }

    [Required]
    public string Title { get; set; }

    public string Description { get; set; }

    public string? Icon { get; set; }
}