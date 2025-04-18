using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class BadgesEarned
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; }

    [Required]
    public string BadgeKey { get; set; }

    public DateTime EarnedDate { get; set; } = DateTime.UtcNow;

    public bool IsNew { get; set; } = true;

    public ApplicationUser User { get; set; }
    public Badge Badge { get; set; }
}
