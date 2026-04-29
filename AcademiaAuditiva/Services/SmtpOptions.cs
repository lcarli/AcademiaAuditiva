namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// SMTP options bound from configuration ("Smtp" section).
    /// In Azure these come from Key Vault as Smtp__Host / Smtp__Port /
    /// Smtp__User / Smtp__Password (case-sensitive on Linux containers).
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
    }
}
