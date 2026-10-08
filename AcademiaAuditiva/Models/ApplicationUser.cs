using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Models;

public class ApplicationUser : IdentityUser
{
    [PersonalData]
    [MaxLength(100)]
    public string FirstName { get; set; }

    [PersonalData]
    [MaxLength(100)]
    public string LastName { get; set; }

    /// <summary>The culture the user signed up in or last switched to (e.g. "pt-BR"), for e-mails sent outside their requests.</summary>
    [PersonalData]
    [MaxLength(10)]
    public string? Language { get; set; }

    /// <summary>True once the user turned off the e-mail sent when a teacher assigns them a routine.</summary>
    [PersonalData]
    public bool RoutineEmailsOff { get; set; }

    // Navegação futura
    public ICollection<BadgesEarned> EarnedBadges { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; }
}