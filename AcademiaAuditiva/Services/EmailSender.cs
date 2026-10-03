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
    public class EmailSender : IEmailSender, IEmailMessageSender
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<SmtpOptions> options, ILogger<EmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, EmailMessage message)
        {
            if (CanSend(email, message.Subject))
            {
                await SendAsync(email, CreateMessage(email, message));
            }
        }

        // Identity's default UI asks for IEmailSender. The site's own pages
        // send composed messages through IEmailMessageSender instead.
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (CanSend(email, subject))
            {
                await SendAsync(email, CreateMessage(email, subject, new BodyBuilder { HtmlBody = htmlMessage }));
            }
        }

        internal MimeMessage CreateMessage(string email, EmailMessage message) =>
            CreateMessage(email, message.Subject,
                new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody });

        private MimeMessage CreateMessage(string email, string subject, BodyBuilder body)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.SenderAddress));
            message.To.Add(new MailboxAddress(string.Empty, email));
            message.Subject = subject;
            message.Body = body.ToMessageBody();
            return message;
        }

        private bool CanSend(string email, string subject)
        {
            if (_options.IsConfigured)
            {
                return true;
            }

            _logger.LogWarning(
                "SMTP is not configured (Smtp:Host, Smtp:User, Smtp:Password and a sender address " +
                "in Smtp:FromAddress are needed). Skipping email to {Email}. Subject: {Subject}",
                LogSanitizer.HashEmail(email), LogSanitizer.Sanitize(subject));
            return false;
        }

        private async Task SendAsync(string email, MimeMessage message)
        {
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
