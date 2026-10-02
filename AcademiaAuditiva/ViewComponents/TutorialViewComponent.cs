using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.ViewComponents;

/// <summary>
/// Renders a guided tour's localized steps for wwwroot/js/core/tutorial.js, which
/// starts the tour on its own until the user has closed it once.
/// </summary>
public sealed class TutorialViewComponent : ViewComponent
{
    // Accented texts stay readable, while <, >, & and quotes are escaped so the
    // JSON cannot close the script element it is rendered in.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly ITutorialService _tutorials;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<TutorialViewComponent> _logger;

    public TutorialViewComponent(
        ITutorialService tutorials,
        UserManager<ApplicationUser> users,
        IStringLocalizer<SharedResources> localizer,
        ILogger<TutorialViewComponent> logger)
    {
        _tutorials = tutorials;
        _users = users;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(string key)
    {
        if (!TutorialCatalog.TryGet(key, out var tutorial))
        {
            throw new ArgumentException($"Unknown tutorial '{key}'.", nameof(key));
        }

        var userId = _users.GetUserId(UserClaimsPrincipal);
        if (userId is null) return Content(string.Empty);

        var autoStart = false;
        try
        {
            autoStart = !await _tutorials.HasSeenAsync(userId, tutorial.Key, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The page still works and the replay button still opens the tour.
            _logger.LogWarning(ex, "Could not load whether the {Tutorial} tour was seen.", tutorial.Key);
        }

        var data = new
        {
            tutorial.Key,
            AutoStart = autoStart,
            Url = Url.Action("Seen", "Tutorial", new { area = "" }),
            Labels = new
            {
                Next = _localizer["Tutorial.Next"].Value,
                Back = _localizer["Tutorial.Back"].Value,
                Skip = _localizer["Tutorial.Skip"].Value,
                Done = _localizer["Tutorial.Done"].Value,
                StepOf = _localizer["Tutorial.StepOf"].Value,
            },
            Steps = tutorial.Steps.Select(step => new
            {
                Title = _localizer[tutorial.TitleKey(step)].Value,
                Text = _localizer[tutorial.TextKey(step)].Value,
                step.Target,
            }),
        };

        return View(new HtmlString(JsonSerializer.Serialize(data, JsonOptions)));
    }
}
