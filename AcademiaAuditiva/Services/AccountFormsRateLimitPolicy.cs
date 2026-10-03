using System.Globalization;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using AcademiaAuditiva.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Services;

/// <summary>
/// Limits how often one client IP sends the account forms, which check passwords and
/// codes or send e-mail. Only POSTs count, so the pages themselves always load. Each
/// replica counts on its own: the lockout stored with the account
/// (<c>IdentityOptions.Lockout</c>) is what caps password guesses.
/// </summary>
public sealed class AccountFormsRateLimitPolicy : IRateLimiterPolicy<string>
{
    public const string Name = "AccountForms";

    private readonly AccountFormsRateLimitOptions _options;

    public AccountFormsRateLimitPolicy(IOptions<AccountFormsRateLimitOptions> options) => _options = options.Value;

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => WriteTooManyAttemptsAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        if (!HttpMethods.IsPost(httpContext.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        // The client's address as the ingress forwarded it (UseForwardedHeaders).
        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = _options.PermitLimit,
            Window = _options.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    // The status is already 429. A short page in the visitor's language links back to
    // the form, whose GET isn't limited; the query keeps the return address.
    private static async ValueTask WriteTooManyAttemptsAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var localizer = http.RequestServices.GetRequiredService<IStringLocalizer<SharedResources>>();
        var html = HtmlEncoder.Default;
        var language = html.Encode(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var title = html.Encode(localizer["RateLimit.Account.Title"]);
        var text = html.Encode(localizer["RateLimit.Account.Text"]);
        var back = html.Encode(localizer["RateLimit.Account.Back"]);
        var form = html.Encode(http.Request.PathBase + http.Request.Path + http.Request.QueryString);

        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="{language}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{title} - Academia Auditiva</title>
            </head>
            <body style="font-family: system-ui, sans-serif; line-height: 1.5; max-width: 36rem; margin: 3rem auto; padding: 0 1rem">
            <h1>{title}</h1>
            <p>{text}</p>
            <p><a href="{form}">{back}</a></p>
            </body>
            </html>
            """, cancellationToken);
    }
}
