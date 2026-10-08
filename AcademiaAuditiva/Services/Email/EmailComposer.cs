using System.Globalization;
using AcademiaAuditiva.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.Email;

/// <summary>
/// Builds the e-mails the site sends. <see cref="EmailLayout"/> renders the
/// HTML and the same texts make the plain-text body. The texts come from
/// SharedResources in the current UI culture: that of the request that sends
/// the e-mail, or the recipient's language for e-mails sent in the background
/// (see <see cref="Routines.RoutineEmails"/>).
/// </summary>
public sealed class EmailComposer
{
    /// <summary>E-mail is read away from the site, so its links and images use the production address.</summary>
    public const string SiteUrl = "https://academiaauditiva.com";

    public const string LogoUrl = SiteUrl + "/apple-touch-icon.png";

    /// <summary>The student's routines.</summary>
    public const string MyTrainingUrl = SiteUrl + "/MyTraining";

    /// <summary>Where people choose which e-mails they get.</summary>
    public const string NotificationSettingsUrl = SiteUrl + "/Identity/Account/Manage/Notifications";

    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IStringLocalizer<SharedResources> _l;

    public EmailComposer(IServiceProvider services, ILoggerFactory loggerFactory, IStringLocalizer<SharedResources> localizer)
    {
        _services = services;
        _loggerFactory = loggerFactory;
        _l = localizer;
    }

    public Task<EmailMessage> ConfirmAccountAsync(string link) => AccountEmailAsync("Identity.Email.Confirm", link);

    public Task<EmailMessage> ConfirmEmailChangeAsync(string link) => AccountEmailAsync("Identity.Email.ChangeEmail", link);

    public Task<EmailMessage> ResetPasswordAsync(string link) => AccountEmailAsync("Identity.Email.ResetPassword", link);

    /// <param name="invitedBy">The teacher, as the student will recognize them.</param>
    public Task<EmailMessage> ClassroomInviteAsync(string invitedBy, string classroom, string link, DateTime expiresAt) =>
        ComposeAsync(
            subject: _l["Invite.Email.Subject", classroom],
            title: _l["Invite.Email.Title"],
            intro: _l["Invite.Email.Intro", invitedBy],
            highlight: classroom,
            actionText: _l["Invite.Email.Button"],
            actionUrl: link,
            notes: [_l["Invite.Email.Expires", expiresAt.ToString("D", CultureInfo.CurrentCulture)], _l["Invite.Email.NoAccount"]],
            reason: _l["Invite.Email.Reason"]);

    /// <summary>Tells a student about a routine their teacher assigned them.</summary>
    /// <param name="teacher">The teacher, as the student will recognize them.</param>
    /// <param name="dueAt">The due date, if any (no time).</param>
    /// <param name="allowLate">Whether answers are still accepted after <paramref name="dueAt"/>.</param>
    public Task<EmailMessage> RoutineAssignedAsync(string teacher, string classroom, string routine, DateTime? dueAt, bool allowLate) =>
        ComposeAsync(
            subject: _l["RoutineEmail.Subject", routine],
            title: _l["RoutineEmail.Title"],
            intro: _l["RoutineEmail.Intro", teacher, classroom],
            highlight: routine,
            actionText: _l["RoutineEmail.Button"],
            actionUrl: MyTrainingUrl,
            notes: [dueAt is { } due
                ? _l[allowLate ? "RoutineEmail.DueLate" : "RoutineEmail.Due", due.ToString("D", CultureInfo.CurrentCulture)]
                : _l["RoutineEmail.NoDue"]],
            reason: _l["RoutineEmail.Reason"],
            settings: (_l["RoutineEmail.TurnOff"], NotificationSettingsUrl));

    /// <summary>
    /// A teacher as their students will recognize them: "First Last (address)", name and address
    /// together since a student may know only one of them, or whichever of the two is known.
    /// </summary>
    public static string DescribeTeacher(string? firstName, string? lastName, string? email)
    {
        var address = email ?? "";
        var name = $"{firstName} {lastName}".Trim();
        if (name.Length == 0) return address;
        return address.Length == 0 ? name : $"{name} ({address})";
    }

    private Task<EmailMessage> AccountEmailAsync(string key, string link) =>
        ComposeAsync(
            subject: _l[key + ".Subject"],
            title: _l[key + ".Title"],
            intro: _l[key + ".Intro"],
            highlight: null,
            actionText: _l[key + ".Button"],
            actionUrl: link,
            notes: [],
            reason: _l[key + ".Reason"]);

    private async Task<EmailMessage> ComposeAsync(string subject, string title, string intro, string? highlight,
        string actionText, string actionUrl, IReadOnlyList<string> notes, string reason,
        (string Text, string Url)? settings = null)
    {
        var language = CultureInfo.CurrentUICulture.Name;
        var content = new EmailContent(language.Length == 0 ? "en" : language, subject, title, intro, highlight,
            actionText, actionUrl, _l["Email.LinkHint"], notes, reason, _l["Layout.FooterTagline"],
            settings?.Text, settings?.Url);

        return new EmailMessage(subject, await RenderAsync(content), PlainText(content));
    }

    private async Task<string> RenderAsync(EmailContent content)
    {
        await using var renderer = new HtmlRenderer(_services, _loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EmailLayout.Content)] = content,
            });
            var output = await renderer.RenderComponentAsync<EmailLayout>(parameters);
            return output.ToHtmlString();
        });
    }

    // The button's text sits on its own line above the link: no colon, which
    // French would need a no-break space before.
    private static string PlainText(EmailContent content)
    {
        var lines = new List<string> { content.Title, "", content.Intro };
        if (content.Highlight is not null)
        {
            lines.Add(content.Highlight);
        }

        lines.AddRange(["", content.ActionText, content.ActionUrl]);
        if (content.Notes.Count > 0)
        {
            lines.Add("");
            lines.AddRange(content.Notes);
        }

        // "-- " opens the signature, which mail programs may show dimmed.
        lines.AddRange(["", "-- ", content.Reason]);
        if (content.SettingsText is not null && content.SettingsUrl is not null)
        {
            lines.AddRange(["", content.SettingsText, content.SettingsUrl]);
        }

        lines.AddRange(["", $"Academia Auditiva · {content.Tagline}", SiteUrl]);
        return string.Join("\n", lines) + "\n";
    }
}
