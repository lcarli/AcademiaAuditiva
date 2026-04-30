using System;
using System.Security.Cryptography;
using System.Text;

namespace AcademiaAuditiva.Extensions;

/// <summary>
/// Helpers to neutralize user-controlled values before they flow into log
/// sinks. Removes CR/LF (log-forging) and replaces email-shaped values
/// with an opaque cryptographic hash so PII does not leak into structured
/// logs while still allowing correlation across log lines.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Strips CR/LF and other control characters that an attacker could use
    /// to inject fake log lines. Truncates overly long values defensively.
    /// </summary>
    public static string? Sanitize(string? value, int maxLength = 256)
    {
        if (value is null) return null;
        var span = value.AsSpan();
        var buffer = new char[Math.Min(span.Length, maxLength)];
        var written = 0;
        for (var i = 0; i < span.Length && written < buffer.Length; i++)
        {
            var c = span[i];
            buffer[written++] = c switch
            {
                '\r' or '\n' or '\t' => '_',
                _ when char.IsControl(c) => '_',
                _ => c,
            };
        }
        return new string(buffer, 0, written);
    }

    /// <summary>
    /// Returns an opaque, deterministic fingerprint of an email for log
    /// correlation. Output looks like <c>email#a1b2c3d4</c> and contains
    /// no part of the original address. Uses SHA-256 — CodeQL recognises
    /// cryptographic hashes as a barrier for the
    /// <c>cs/exposure-of-sensitive-information</c> taint flow.
    /// </summary>
    public static string HashEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "email#none";
        var normalized = email.Trim().ToLowerInvariant();
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(normalized), hash);
        var sb = new StringBuilder("email#", 14);
        for (var i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }
}

