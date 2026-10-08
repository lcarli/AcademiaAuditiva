namespace AcademiaAuditiva.Services.Email;

/// <summary>
/// The e-mails the site sends on its own, such as the notice of a routine assigned
/// ("NotificationEmails" section). Account e-mails and classroom invites don't count here.
/// </summary>
public sealed class NotificationEmailOptions
{
    public const string Section = "NotificationEmails";

    /// <summary>
    /// How many of these e-mails the site sends per UTC day, all replicas together (see
    /// <see cref="NotificationEmailQuota"/>). Resend's free plan sends 100 e-mails a day in all,
    /// so the default leaves the rest for account e-mails and invites. 0 or less turns them off.
    /// </summary>
    public int DailyLimit { get; set; } = 60;
}
