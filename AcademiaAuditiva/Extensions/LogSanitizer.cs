using System;

namespace AcademiaAuditiva.Extensions;

/// <summary>
/// Helpers to neutralize user-controlled values before they flow into log
/// sinks. Removes CR/LF (log-forging) and masks email-shaped values so
/// PII does not leak into structured logs.
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
    /// Returns a masked form of an email address for log output, e.g.
    /// <c>j***@example.com</c>. Falls back to <c>***</c> when the value is
    /// missing or not email-shaped. Always sanitized for log forging.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var clean = Sanitize(email) ?? "***";
        var at = clean.IndexOf('@');
        if (at <= 0 || at == clean.Length - 1) return "***";
        var local = clean[..at];
        var domain = clean[(at + 1)..];
        var head = local.Length > 0 ? local[0].ToString() : "*";
        return $"{head}***@{domain}";
    }
}
