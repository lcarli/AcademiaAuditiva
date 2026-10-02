using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Models;

/// <summary>
/// A guided tour (see <c>TutorialCatalog</c>) the user has closed, so it no
/// longer starts on its own. One row per user and tour.
/// </summary>
public class UserTutorial
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public IdentityUser? User { get; set; }

    public string TutorialKey { get; set; } = string.Empty;

    /// <summary>When the user first closed the tour.</summary>
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>True once the user reached the last step; false while they only skipped it.</summary>
    public bool Finished { get; set; }
}
