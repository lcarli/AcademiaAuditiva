using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string FirstName { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; }

    // Navegação futura
    public ICollection<BadgesEarned> EarnedBadges { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; }
}