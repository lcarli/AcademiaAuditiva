using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The texts of a routine taken like a test (My Training, the exercise page's routine banner,
/// the answers it refuses, the teacher's late answers option, and the locks on an assigned
/// routine and on a student's started items) exist in every culture.
/// Views render them through IHtmlLocalizer, which runs string.Format even without arguments,
/// so only keys that are always given arguments may hold braces.
/// </summary>
public class RoutineResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Key → number of arguments passed by Views/MyTraining/Index, RoutineRoundStatus and the teacher's RoutinesController.
    private static readonly Dictionary<string, int> FormattedKeys = new()
    {
        ["MyTraining.Answered"] = 2,
        ["Routine.Question"] = 2,
        ["Routine.Result"] = 3,
        ["Routine.Passed"] = 1,
        ["Routine.BelowMin"] = 1,
        ["Teacher.Routines.CopyName"] = 1,
    };

    private static readonly string[] PlainKeys =
    [
        "MyTraining.Finished", "MyTraining.Closed",
        "Routine.Late", "Routine.Closed", "Routine.ClosedTitle", "Routine.ItemDone", "Routine.ItemDoneTitle",
        "Routine.AlreadyAnswered", "Routine.AlreadyAnsweredTitle", "Routine.Unavailable", "Routine.UnavailableTitle",
        "Routine.BackToMyTraining", "Routine.FilterLocked",
        "Teacher.Routines.AllowLate", "Teacher.Routines.AllowLateHelp",
        "Teacher.Routines.Duplicate", "Teacher.Routines.Locked",
        "Teacher.Overrides.Started", "Teacher.Overrides.ClearStartedConfirm",
        "Toast.RoutineLocked", "Toast.RoutineDuplicated", "Toast.RoutineEmpty", "Toast.OverridesStartedKept",
    ];

    private static IEnumerable<string> AllKeys => PlainKeys.Concat(FormattedKeys.Keys);

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
    public void TextsWithArguments_UseEachArgument(string culture)
    {
        var resources = Resources(culture);

        foreach (var (key, arguments) in FormattedKeys)
        {
            resources.Should().ContainKey(key);
            var value = resources[key];
            Regex.Matches(value, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .Distinct().Order()
                .Should().Equal(Enumerable.Range(0, arguments), "{0} takes {1} argument(s)", key, arguments);
            var format = () => string.Format(CultureInfo.InvariantCulture, value, new object[arguments]);
            format.Should().NotThrow(key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void RoutineTexts_AreOnlyThoseThePagesUse(string culture)
    {
        Resources(culture).Keys.Where(key => key.StartsWith("Routine.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(AllKeys.Where(key => key.StartsWith("Routine.", StringComparison.Ordinal)));
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

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in AllKeys)
        {
            resources[key].Should().NotMatchRegex(@"[ \S][:!?%]", "{0} needs a no-break space before : ! ? %", key)
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
