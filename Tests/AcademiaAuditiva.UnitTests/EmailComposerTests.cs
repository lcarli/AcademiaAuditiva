using System.Globalization;
using System.Net;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The e-mails the site sends: one branded HTML layout and the same texts as
/// plain text, in the culture of the request that sends them. Mail programs
/// load nothing but images, and some show only the text.
/// </summary>
public class EmailComposerTests
{
    // Identity's links carry '&' and Base64 codes: encoded in the HTML, as is in the text.
    private const string Link = "https://academiaauditiva.com/Identity/Account/ResetPassword?code=Q2+Rl/Zg==&userId=42";

    private static readonly ResourceManager Texts = new(typeof(SharedResources));

    public static TheoryData<string, string> AccountEmails => new()
    {
        { "en-US", "Confirm" }, { "en-US", "ResetPassword" }, { "en-US", "ChangeEmail" },
        { "pt-BR", "Confirm" }, { "pt-BR", "ResetPassword" }, { "pt-BR", "ChangeEmail" },
        { "fr-CA", "Confirm" }, { "fr-CA", "ResetPassword" }, { "fr-CA", "ChangeEmail" },
    };

    public static TheoryData<string> Cultures => new() { "en-US", "pt-BR", "fr-CA" };

    [Theory]
    [MemberData(nameof(AccountEmails))]
    public async Task AccountEmails_SpeakTheLanguageOfTheRequest(string culture, string kind)
    {
        UseCulture(culture);

        var message = kind switch
        {
            "Confirm" => await Composer().ConfirmAccountAsync(Link),
            "ResetPassword" => await Composer().ResetPasswordAsync(Link),
            _ => await Composer().ConfirmEmailChangeAsync(Link),
        };

        var key = $"Identity.Email.{kind}";
        message.Subject.Should().Be(Text(key + ".Subject", culture));
        ShouldShow(message, culture,
            Text(key + ".Title", culture), Text(key + ".Intro", culture),
            Text(key + ".Button", culture), Text(key + ".Reason", culture));
        message.TextBody.Should().NotContain("<", "the text body is no HTML");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task ClassroomInvite_ShowsWhatTheTeacherTyped_AsText(string culture)
    {
        UseCulture(culture);
        const string teacher = "Ana \"<b>Lima</b>\" & Co (ana@example.test)";
        const string classroom = "<script>alert('x')</script> Ear & Rhythm";
        var expires = new DateTime(2026, 3, 9, 15, 0, 0, DateTimeKind.Utc);

        var message = await Composer().ClassroomInviteAsync(teacher, classroom, Link, expires);

        message.Subject.Should().Be(string.Format(Text("Invite.Email.Subject", culture), classroom));
        message.HtmlBody.Should().NotContain("<b>").And.Contain("&lt;script&gt;");
        ShouldShow(message, culture,
            Text("Invite.Email.Title", culture),
            string.Format(Text("Invite.Email.Intro", culture), teacher),
            classroom,
            Text("Invite.Email.Button", culture),
            string.Format(Text("Invite.Email.Expires", culture), expires.ToString("D", CultureInfo.GetCultureInfo(culture))),
            Text("Invite.Email.NoAccount", culture),
            Text("Invite.Email.Reason", culture));
    }

    [Fact]
    public async Task PlainText_FollowsTheHtml()
    {
        UseCulture("en-US");
        var expires = new DateTime(2026, 3, 9, 15, 0, 0, DateTimeKind.Utc);

        var message = await Composer().ClassroomInviteAsync("Ana Lima (ana@example.test)", "Ear training", Link, expires);

        message.TextBody.Should().Be(string.Join("\n",
        [
            Text("Invite.Email.Title", "en-US"),
            "",
            string.Format(Text("Invite.Email.Intro", "en-US"), "Ana Lima (ana@example.test)"),
            "Ear training",
            "",
            Text("Invite.Email.Button", "en-US"),
            Link,
            "",
            string.Format(Text("Invite.Email.Expires", "en-US"), "Monday, March 9, 2026"),
            Text("Invite.Email.NoAccount", "en-US"),
            "",
            "-- ",
            Text("Invite.Email.Reason", "en-US"),
            "",
            "Academia Auditiva · " + Text("Layout.FooterTagline", "en-US"),
            "https://academiaauditiva.com",
            "",
        ]));
    }

    // What both bodies show, and what the HTML may load.
    private static void ShouldShow(EmailMessage message, string culture, params string[] texts)
    {
        var html = message.HtmlBody;
        html.Should().StartWith("<!DOCTYPE html>").And.Contain($"<html lang=\"{culture}\"");
        html.Should().NotContainAny(["<script", "<link", "@font-face", "{0}"], "mail programs block them");
        html.Should().Contain("<!--[if mso]>", "classic Outlook needs its fixed-width column");
        Attributes(html, "src").Should().NotBeEmpty()
            .And.OnlyContain(src => src.StartsWith(EmailComposer.SiteUrl + "/"), "images load from the site");
        Attributes(html, "href").Should().BeEquivalentTo([Link, Link, EmailComposer.SiteUrl],
            "the button and the copyable link lead to the same page, the footer to the site");

        var shown = WebUtility.HtmlDecode(html);
        foreach (var text in texts.Append(message.Subject)
                     .Append(Text("Email.LinkHint", culture)).Append(Text("Layout.FooterTagline", culture)))
        {
            shown.Should().Contain(text);
        }

        foreach (var text in texts)
        {
            message.TextBody.Should().Contain(text);
        }

        message.TextBody.Should().Contain(Link).And.EndWith(EmailComposer.SiteUrl + "\n");
    }

    private static IEnumerable<string> Attributes(string html, string name) =>
        Regex.Matches(html, $"\\s{name}=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value));

    private static string Text(string key, string culture) =>
        Texts.GetString(key, CultureInfo.GetCultureInfo(culture)) ?? throw new KeyNotFoundException(key);

    // Flows with the calling test's async context only.
    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    }

    private static EmailComposer Composer() => new(
        new ServiceCollection().BuildServiceProvider(),
        NullLoggerFactory.Instance,
        new StringLocalizer<SharedResources>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)));
}
