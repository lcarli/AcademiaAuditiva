using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;
using System.Threading.Tasks;

namespace AcademiaAuditiva.Services
{
    public class EmailSender : IEmailSender
    {
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Academia Auditiva", "academiaauditiva@gmail.com"));
            message.To.Add(new MailboxAddress("", email));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlMessage
            };

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            await client.ConnectAsync("smtp.gmail.com", 587, false);
            await client.AuthenticateAsync("academiaauditiva@gmail.com", "dqnszabfutbuouev");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
