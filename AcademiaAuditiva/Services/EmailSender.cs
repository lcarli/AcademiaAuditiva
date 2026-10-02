using AcademiaAuditiva.Extensions;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System;
using System.Threading.Tasks;

namespace AcademiaAuditiva.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<SmtpOptions> options, ILogger<EmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (!_options.IsConfigured)
            {
                _logger.LogWarning(
                    "SMTP is not configured (Smtp:Host, Smtp:User, Smtp:Password and a sender address " +
                    "in Smtp:FromAddress are needed). Skipping email to {Email}. Subject: {Subject}",
                    LogSanitizer.HashEmail(email), LogSanitizer.Sanitize(subject));
                return;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.SenderAddress));
            message.To.Add(new MailboxAddress(string.Empty, email));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlMessage }.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            try
            {
                await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl);
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                await client.AuthenticateAsync(_options.User, _options.Password);
                await client.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", LogSanitizer.HashEmail(email));
                throw;
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true);
                }
            }
        }
    }
}
