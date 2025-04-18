using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class Subscription
{
    public int Id { get; set; }

    public string UserId { get; set; }
    public ApplicationUser User { get; set; }

    [Required]
    public string Plan { get; set; }

    [Required]
    public string Status { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string Gateway { get; set; }
}
