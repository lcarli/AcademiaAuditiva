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

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task RoutineAssigned_LeadsToMyTraining_AndToTurningItOff(string culture)
    {
        UseCulture(culture);
        const string teacher = "Ana \"<b>Lima</b>\" (ana@example.test)";
        const string classroom = "<i>Ear</i> & Rhythm";
        const string routine = "<script>alert('x')</script> Week 1";
        var due = new DateTime(2026, 2, 1);

        var message = await Composer().RoutineAssignedAsync(teacher, classroom, routine, due, allowLate: false);

        message.Subject.Should().Be(string.Format(Text("RoutineEmail.Subject", culture), routine));
        message.HtmlBody.Should().NotContainAny("<b>", "<i>").And.Contain("&lt;script&gt;");
        ShouldShowLinking(message, culture, EmailComposer.MyTrainingUrl,
            [EmailComposer.MyTrainingUrl, EmailComposer.MyTrainingUrl, EmailComposer.NotificationSettingsUrl, EmailComposer.SiteUrl],
            [
                Text("RoutineEmail.Title", culture),
                string.Format(Text("RoutineEmail.Intro", culture), teacher, classroom),
                routine,
                Text("RoutineEmail.Button", culture),
                string.Format(Text("RoutineEmail.Due", culture), due.ToString("D", CultureInfo.GetCultureInfo(culture))),
                Text("RoutineEmail.Reason", culture),
                Text("RoutineEmail.TurnOff", culture),
            ]);
        message.TextBody.Should().Contain(EmailComposer.NotificationSettingsUrl);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task RoutineWithoutDueDate_SaysThereIsNone(string culture)
    {
        UseCulture(culture);

        var message = await Composer().RoutineAssignedAsync("Ana Lima", "Ear training", "Week 1", dueAt: null, allowLate: true);

        WebUtility.HtmlDecode(message.HtmlBody).Should().Contain(Text("RoutineEmail.NoDue", culture));
        message.TextBody.Should().Contain(Text("RoutineEmail.NoDue", culture));
    }

    [Fact]
    public async Task RoutinePlainText_SaysLateAnswersCount_AndHowToTurnItOff()
    {
        UseCulture("en-US");

        var message = await Composer().RoutineAssignedAsync(
            "Ana Lima (ana@example.test)", "Ear training", "Week 1", new DateTime(2026, 2, 1), allowLate: true);

        message.TextBody.Should().Be(string.Join("\n",
        [
            Text("RoutineEmail.Title", "en-US"),
            "",
            string.Format(Text("RoutineEmail.Intro", "en-US"), "Ana Lima (ana@example.test)", "Ear training"),
            "Week 1",
            "",
            Text("RoutineEmail.Button", "en-US"),
            EmailComposer.MyTrainingUrl,
            "",
            string.Format(Text("RoutineEmail.DueLate", "en-US"), "Sunday, February 1, 2026"),
            "",
            "-- ",
            Text("RoutineEmail.Reason", "en-US"),
            "",
            Text("RoutineEmail.TurnOff", "en-US"),
            EmailComposer.NotificationSettingsUrl,
            "",
            "Academia Auditiva · " + Text("Layout.FooterTagline", "en-US"),
            "https://academiaauditiva.com",
            "",
        ]));
    }

    [Theory]
    [InlineData("Ana", "Lima", "ana@example.test", "Ana Lima (ana@example.test)")]
    [InlineData(null, "Lima", "ana@example.test", "Lima (ana@example.test)")]
    [InlineData(" ", "", "ana@example.test", "ana@example.test")]
    [InlineData("Ana", "Lima", null, "Ana Lima")]
    [InlineData(null, null, null, "")]
    public void Teacher_IsNamedWithTheirAddress(string? firstName, string? lastName, string? email, string expected) =>
        EmailComposer.DescribeTeacher(firstName, lastName, email).Should().Be(expected);

    // What both bodies show, and what the HTML may load.
    private static void ShouldShow(EmailMessage message, string culture, params string[] texts) =>
        ShouldShowLinking(message, culture, Link, [Link, Link, EmailComposer.SiteUrl], texts);

    // hrefs: the button's and the copyable link's (both link), then the footer's.
    private static void ShouldShowLinking(EmailMessage message, string culture, string link, string[] hrefs, string[] texts)
    {
        var html = message.HtmlBody;
        html.Should().StartWith("<!DOCTYPE html>").And.Contain($"<html lang=\"{culture}\"");
        html.Should().NotContainAny(["<script", "<link", "@font-face", "{0}", "{1}"], "mail programs block them");
        html.Should().Contain("<!--[if mso]>", "classic Outlook needs its fixed-width column");
        Attributes(html, "src").Should().NotBeEmpty()
            .And.OnlyContain(src => src.StartsWith(EmailComposer.SiteUrl + "/"), "images load from the site");
        Attributes(html, "href").Should().Equal(hrefs,
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

        message.TextBody.Should().Contain(link).And.EndWith(EmailComposer.SiteUrl + "\n");
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
