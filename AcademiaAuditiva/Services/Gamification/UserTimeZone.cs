using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Services.Gamification;

/// <summary>
/// The browser stores its IANA time zone in the <c>aa_tz</c> cookie (see
/// _Layout.cshtml) so practice streaks follow the player's calendar days.
/// </summary>
public static partial class UserTimeZone
{
    public const string CookieName = "aa_tz";

    public static TimeZoneInfo FromRequest(HttpRequest request) => Parse(request.Cookies[CookieName]);

    /// <summary>Resolves an IANA id such as "America/Sao_Paulo"; anything unknown or malformed falls back to UTC.</summary>
    public static TimeZoneInfo Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TimeZoneInfo.Utc;
        if (value.Contains('%')) value = Uri.UnescapeDataString(value);
        if (!AllowedId().IsMatch(value)) return TimeZoneInfo.Utc;

        return TimeZoneInfo.TryFindSystemTimeZoneById(value, out var timeZone) ? timeZone : TimeZoneInfo.Utc;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_+\-/]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedId();
}
