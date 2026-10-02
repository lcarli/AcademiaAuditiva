using System.Net.Mail;

namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// SMTP options bound from configuration ("Smtp" section).
    /// In Azure these come from the Key Vault secrets Smtp--Host, Smtp--Port,
    /// Smtp--User, Smtp--Password and Smtp--FromAddress; production sends
    /// through Resend's SMTP server.
    /// </summary>
    public class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 465;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "Academia Auditiva";
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// The address mail goes out from: <see cref="FromAddress"/>, or else
        /// <see cref="User"/> when that is an e-mail address. Resend signs in
        /// as "resend", so it needs FromAddress.
        /// </summary>
        public string SenderAddress =>
            !string.IsNullOrWhiteSpace(FromAddress) ? FromAddress.Trim()
            : !string.IsNullOrWhiteSpace(User) && User.Contains('@') ? User.Trim()
            : string.Empty;

        /// <summary>
        /// True when mail can be sent: a server, credentials and a plain sender
        /// address. Otherwise the app skips sending and shows confirmation
        /// links on screen.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) &&
            !string.IsNullOrWhiteSpace(User) &&
            !string.IsNullOrWhiteSpace(Password) &&
            MailAddress.TryCreate(SenderAddress, out var sender) &&
            sender.Address == SenderAddress;
    }
}
