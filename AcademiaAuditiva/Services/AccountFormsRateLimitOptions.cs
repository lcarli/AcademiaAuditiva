namespace AcademiaAuditiva.Services;

/// <summary>
/// How many account forms (sign-in, two-factor, registration and the e-mail forms)
/// one client IP may send per <see cref="Window"/>. Bound from the
/// "RateLimiting:AccountForms" section; the integration tests raise the limit.
/// </summary>
public sealed class AccountFormsRateLimitOptions
{
    public const string SectionName = "RateLimiting:AccountForms";

    // A class behind one school address signing in at once stays well under it.
    public int PermitLimit { get; set; } = 30;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
