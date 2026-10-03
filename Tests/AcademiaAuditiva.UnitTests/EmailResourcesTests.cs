using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Texts of the e-mails, in every culture. EmailLayout encodes them, so they
/// hold no markup, and only the keys always given an argument may hold braces.
/// </summary>
public class EmailResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Given the classroom name, the teacher or the expiry date by EmailComposer.
    private static readonly string[] FormattedKeys =
        ["Invite.Email.Subject", "Invite.Email.Intro", "Invite.Email.Expires"];

    private static readonly string[] PlainKeys =
    [
        "Email.LinkHint",
        .. new[] { "Confirm", "ResetPassword", "ChangeEmail" }.SelectMany(kind =>
            new[] { "Subject", "Title", "Intro", "Button", "Reason" }.Select(part => $"Identity.Email.{kind}.{part}")),
        "Invite.Email.Title", "Invite.Email.Button", "Invite.Email.NoAccount", "Invite.Email.Reason",
    ];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Texts_ExistWithoutMarkup(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in PlainKeys.Concat(FormattedKeys))
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[<>]", "{0} is encoded as text", key);
        }

        foreach (var key in PlainKeys)
        {
            resources[key].Should().NotMatchRegex("[{}]", "{0} is shown without arguments", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithAnArgument_UseItOnce(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in FormattedKeys)
        {
            Regex.Matches(resources[key], @"\{[^{}]*\}|[{}]").Select(m => m.Value).Should().Equal(["{0}"],
                "{0} shows its argument once and nothing else", key);
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EveryText_IsTranslated(string culture)
    {
        var english = Resources("");
        var translated = Resources(culture);

        foreach (var key in PlainKeys.Concat(FormattedKeys))
        {
            translated[key].Should().NotBe(english[key], "{0} needs a {1} text", key, culture);
        }
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in PlainKeys.Concat(FormattedKeys))
        {
            resources[key].Should().NotMatchRegex(@"[ \S][:!?]", "{0} needs a no-break space before : ! ?", key)
                .And.NotContain("'", "{0} uses typographic apostrophes", key);
        }
    }

    private static Dictionary<string, string> Resources(string culture)
    {
        var set = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the {0} resources are deployed", culture);
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
