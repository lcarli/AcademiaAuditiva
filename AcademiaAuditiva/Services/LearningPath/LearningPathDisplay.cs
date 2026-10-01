using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.LearningPath;

/// <summary>Links and texts shared by the path page, the dashboard card and the answer feedback.</summary>
public static class LearningPathDisplay
{
    /// <summary>The step's exercise page with its preset in the query string, like routine links.</summary>
    public static string StepUrl(this IUrlHelper url, StepProgress step)
    {
        var values = new RouteValueDictionary(step.Filters.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
        {
            ["area"] = string.Empty,
        };
        return url.Action(step.Exercise, "Exercise", values)
            ?? url.Action("Index", "Exercise", new { area = string.Empty })
            ?? "/Exercise";
    }

    public static string StepTitle(this IStringLocalizer localizer, StepProgress step)
    {
        var text = localizer[step.Exercise];
        return text.ResourceNotFound ? step.Exercise : text.Value;
    }

    public static string StepSubtitle(this IStringLocalizer localizer, StepProgress step)
    {
        var text = localizer[$"Exercise.{step.Exercise}.Subtitle"];
        return text.ResourceNotFound ? string.Empty : text.Value;
    }
}
