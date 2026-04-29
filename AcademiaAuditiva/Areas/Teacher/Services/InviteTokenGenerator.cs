using System.Security.Cryptography;

namespace AcademiaAuditiva.Areas.Teacher.Services;

public static class InviteTokenGenerator
{
    /// <summary>
    /// Generates a 64-character URL-safe random token (256 bits of entropy).
    /// </summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
