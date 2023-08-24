using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;
using System.Net.Mail;
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

			using (var client = new MailKit.Net.Smtp.SmtpClient())
			{
				try
				{
					client.Connect("smtp.gmail.com", 465, true);
					client.AuthenticationMechanisms.Remove("XOAUTH2");
					client.Authenticate("academiaauditiva@gmail.com", "dqnszabfutbuouev");
					await client.SendAsync(message);
				}
				catch (Exception ex)
				{
					throw;
				}
				finally
				{
					client.Disconnect(true);
					client.Dispose();
				}
			}
		}
    }
}
