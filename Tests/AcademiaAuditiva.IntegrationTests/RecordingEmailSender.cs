using System.Collections.Concurrent;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>Records the e-mails the site sends, so no test ever reaches a real mail server; with <paramref name="fail"/>, every one fails after being recorded.</summary>
internal sealed class RecordingEmailSender(bool fail = false) : IEmailMessageSender, IEmailSender
{
    public ConcurrentQueue<(string To, EmailMessage Message)> Sent { get; } = new();

    public IEnumerable<string> Recipients => Sent.Select(s => s.To);

    public Task SendEmailAsync(string email, EmailMessage message)
    {
        Sent.Enqueue((email, message));
        return fail ? Task.FromException(new InvalidOperationException("SMTP is down")) : Task.CompletedTask;
    }

    // Only Identity's own pages send bare HTML, and the site replaces every one that sends e-mail.
    public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        throw new InvalidOperationException("The site's pages send composed messages.");
}
