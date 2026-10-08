namespace AcademiaAuditiva.Models;

/// <summary>
/// How many notification e-mails were sent on a UTC day. Every replica counts in the same row,
/// so the daily cap (<c>NotificationEmails:DailyLimit</c>) holds across them.
/// </summary>
public class EmailDailyCount
{
    public DateOnly Day { get; set; }

    /// <summary>The e-mails reserved that day; a concurrency token, so two replicas never both add to the same count.</summary>
    public int Sent { get; set; }
}
