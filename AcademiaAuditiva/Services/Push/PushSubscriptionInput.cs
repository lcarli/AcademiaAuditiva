using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace AcademiaAuditiva.Services.Push;

public sealed class PushSubscriptionInput : IValidatableObject
{
    [Required, MaxLength(2048)]
    public string Endpoint { get; set; } = "";
    [Required, MaxLength(128)]
    public string P256dh { get; set; } = "";
    [Required, MaxLength(64)]
    public string Auth { get; set; } = "";
    [Required, MaxLength(64)]
    public string TimeZoneId { get; set; } = "";
    [Range(0, 1439)]
    public int MinuteOfDay { get; set; }

    // Never send an authenticated server request to an arbitrary user-supplied URL.
    public static bool IsAllowedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0) return false;
        return uri.Host is "web.push.apple.com" or "fcm.googleapis.com" or "updates.push.services.mozilla.com"
            || uri.Host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsAllowedEndpoint(Endpoint) || !ValidKeys()
            || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out _))
            yield return new ValidationResult("Push.Invalid");
    }

    private bool ValidKeys()
    {
        try
        {
            var key = WebEncoders.Base64UrlDecode(P256dh);
            if (key.Length != 65 || key[0] != 4 || WebEncoders.Base64UrlDecode(Auth).Length != 16) return false;
            using var publicKey = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key[1..33], Y = key[33..65] }
            });
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
