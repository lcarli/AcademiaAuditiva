namespace AcademiaAuditiva.Services;

/// <summary>
/// Sends an <see cref="EmailMessage"/> with both its HTML and plain-text
/// bodies. Build the message with <see cref="Email.EmailComposer"/>.
/// </summary>
public interface IEmailMessageSender
{
    Task SendEmailAsync(string email, EmailMessage message);
}
