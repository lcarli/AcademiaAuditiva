using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Models;

public sealed class StudyPushSubscription
{
    public long Id { get; set; }
    [MaxLength(450)]
    public string UserId { get; set; } = "";
    public IdentityUser? User { get; set; }
    [MaxLength(64)]
    public string EndpointHash { get; set; } = "";
    [MaxLength(2048)]
    public string Endpoint { get; set; } = "";
    [MaxLength(128)]
    public string P256dh { get; set; } = "";
    [MaxLength(64)]
    public string Auth { get; set; } = "";
    [MaxLength(64)]
    public string TimeZoneId { get; set; } = "UTC";
    public int MinuteOfDay { get; set; }
    public DateOnly? LastReminderDate { get; set; }
    public DateTime LeaseUntil { get; set; }
}
