using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace AcademiaAuditiva.Services.Gamification;

/// <summary>Badge art shared by the badge pages and the answer rewards (wwwroot/js/core/rewards.js).</summary>
public static class BadgeDisplay
{
    /// <summary>
    /// URL of the badge's medal, wwwroot/img/badges/{key}.webp (made by
    /// scripts/export-badge-art.py), with a hash of the file appended like
    /// asp-append-version, so browsers fetch new art as soon as it ships.
    /// </summary>
    public static string BadgeImage(this IUrlHelper url, string key)
    {
        var http = url.ActionContext.HttpContext;
        return http.RequestServices.GetRequiredService<IFileVersionProvider>()
            .AddFileVersionToPath(http.Request.PathBase, url.Content($"~/img/badges/{key}.webp"));
    }
}
