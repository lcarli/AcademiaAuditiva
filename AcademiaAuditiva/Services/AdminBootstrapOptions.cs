namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// Configuration for the bootstrap admin account that the application
    /// seeds at startup when missing. Bound from the "Admin" section.
    /// In Azure these come from configuration env vars (Admin__Email,
    /// Admin__InitialPassword). InitialPassword is consumed only on the very
    /// first run; afterwards the user changes it and the value is ignored.
    /// </summary>
    public class AdminBootstrapOptions
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = "Admin";
        public string LastName { get; set; } = "User";
        /// <summary>
        /// Optional initial password. If empty the admin user is still
        /// created (EmailConfirmed=true) and must use "forgot password"
        /// to set a password — this is the recommended secure flow.
        /// </summary>
        public string InitialPassword { get; set; } = string.Empty;
    }
}
