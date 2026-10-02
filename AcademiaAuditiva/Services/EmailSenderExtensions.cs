using Microsoft.AspNetCore.Identity.UI.Services;

namespace AcademiaAuditiva.Services;

public static class EmailSenderExtensions
{
    /// <summary>
    /// Sends an e-mail without failing the request when the mail provider is
    /// down. Sign-up must not end on an error page after the account was
    /// created, and the account pages must not answer differently for real
    /// accounts. <see cref="EmailSender"/> logs the failure.
    /// </summary>
    /// <returns>False when sending failed.</returns>
    public static async Task<bool> TrySendEmailAsync(this IEmailSender sender, string email, string subject, string htmlMessage)
    {
        try
        {
            await sender.SendEmailAsync(email, subject, htmlMessage);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
