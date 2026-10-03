using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Texts of the admin lock, unlock and delete actions, of the lockout page and of the
/// page refusing too many account forms, in every culture. Views render them through
/// IHtmlLocalizer, which runs string.Format even without arguments, so only keys
/// always given the user name or a number of minutes may hold braces.
/// </summary>
public class AdminUsersResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Given the user name by UsersController and Users/Index.cshtml.
    private static readonly string[] FormattedKeys =
    [
        "Admin.Users.LockConfirm", "Admin.Users.UnlockConfirm", "Admin.Users.LockedUser", "Admin.Users.UnlockedUser",
        "Admin.Users.DeletedUser", "Admin.Users.UpdateFailed", "Admin.Users.DeleteFailed",
    ];

    // Given the minutes a lockout after wrong sign-ins lasts, by Users/Index.cshtml and the lockout page.
    private static readonly string[] MinuteKeys = ["Admin.Users.LockedFor", "Lockout.Wait"];

    private static readonly string[] PlainKeys =
    [
        "Admin.Users.Lock", "Admin.Users.Unlock", "Admin.Users.Delete", "Admin.Users.CannotLockSelf",
        "Admin.Users.CannotLockAdmin", "Admin.Users.CannotDeleteSelf", "Admin.Users.CannotDeleteAdmin",
        "Admin.Users.Delete.Title", "Admin.Users.Delete.Warning", "Admin.Users.Delete.TeachingWarning",
        "Admin.Users.Delete.Submit", "Lockout.Text", "Lockout.Contact",
        "RateLimit.Account.Title", "RateLimit.Account.Text", "RateLimit.Account.Back",
    ];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithoutArguments_ExistAndHaveNoBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in PlainKeys)
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[{}]", "{0} is rendered without arguments", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithTheUserName_UseIt(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in FormattedKeys)
        {
            resources.Should().ContainKey(key);
            Regex.Matches(resources[key], @"\{[^{}]*\}|[{}]").Select(m => m.Value).Should().Equal(["{0}"],
                "{0} shows the user name once and nothing else", key);
            string.Format(CultureInfo.InvariantCulture, resources[key], "ana@example.com").Should().Contain("ana@example.com");
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithMinutes_UseThem(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in MinuteKeys)
        {
            resources.Should().ContainKey(key);
            Regex.Matches(resources[key], @"\{[^{}]*\}|[{}]").Select(m => m.Value).Should().Equal(["{0}"],
                "{0} shows the minutes once and nothing else", key);
            string.Format(CultureInfo.InvariantCulture, resources[key], 15).Should().Contain("15");
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EveryText_IsTranslated(string culture)
    {
        var english = Resources("");
        var translated = Resources(culture);

        foreach (var key in AllKeys)
        {
            translated[key].Should().NotBe(english[key], "{0} needs a {1} text", key, culture);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void CannotDeleteSelf_NamesTheMenusAsShown(string culture)
    {
        var resources = Resources(culture);

        resources["Admin.Users.CannotDeleteSelf"].Should()
            .Contain(resources["Login.ManageAccount"]).And.Contain(resources["Manage.Nav.PersonalData"]);
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in AllKeys)
        {
            resources[key].Should().NotMatchRegex(@"[ \S][:!?]", "{0} needs a no-break space before : ! ?", key)
                .And.NotContain("'", "{0} uses typographic apostrophes", key);
        }
    }

    private static IEnumerable<string> AllKeys => PlainKeys.Concat(FormattedKeys).Concat(MinuteKeys);

    private static Dictionary<string, string> Resources(string culture)
    {
        var set = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the {0} resources are deployed", culture);
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
