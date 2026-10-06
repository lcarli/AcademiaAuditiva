using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The texts of the teacher's reports (#107) exist in every culture, and those of the dashboard
/// they replace are gone. Views render them through IHtmlLocalizer, which runs string.Format even
/// without arguments, so only keys that are always given arguments may hold braces.
/// </summary>
public class TeacherReportResourcesTests
{
    private const string Prefix = "Teacher.Reports.";

    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Key → number of arguments passed by the views in Areas/Teacher/Views/Dashboard.
    private static readonly Dictionary<string, int> FormattedKeys = new()
    {
        [Prefix + "Seconds"] = 1,
    };

    private static readonly string[] PlainKeys =
    [
        .. new[]
        {
            "Link", "AssignmentTitle", "ClassroomTitle", "StudentTitle", "Back", "PrivacyNote", "Students",
            "Status.Finished", "Status.InProgress", "Status.NotStarted", "Late", "FinishedCount", "NotStartedCount",
            "Excluded", "Accuracy", "ByExercise", "ByStudent", "ByRoutine",
            "Col.Exercise", "Col.FinishedBy", "Col.Answers", "Col.TimePerAnswer", "Col.Student", "Col.Status",
            "Col.FinishedAt", "Col.Routine", "Col.Assigned", "Col.Due", "Col.RoutinesFinished", "Col.LastAnswer",
            "NoStudents", "NoRoutines", "NoMembers", "StudentNoRoutines",
        }.Select(key => Prefix + key),
        "Home.Roles.Teacher3.Desc",
    ];

    // The same word in English: a number of seconds, and "Routine" in French.
    private static readonly Dictionary<string, string[]> SameAsEnglish = new()
    {
        ["pt-BR"] = [Prefix + "Seconds"],
        ["fr-CA"] = [Prefix + "Seconds", Prefix + "Col.Routine"],
    };

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
    public void Seconds_KeepTheirUnitOnTheSameLine(string culture)
    {
        Resources(culture)[Prefix + "Seconds"].Should().Be("{0}\u00a0s");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ReportTexts_AreOnlyThoseThePagesUse(string culture)
    {
        Resources(culture).Keys.Where(key => key.StartsWith(Prefix, StringComparison.Ordinal))
            .Should().BeEquivalentTo(AllKeys.Where(key => key.StartsWith(Prefix, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TheOldDashboardTexts_AreGone(string culture)
    {
        Resources(culture).Keys
            .Where(key => key.StartsWith("Teacher.Dashboard.", StringComparison.Ordinal) || key == "Teacher.Classrooms.Dashboard")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EveryText_IsTranslated(string culture)
    {
        var english = Resources("");
        var translated = Resources(culture);

        foreach (var key in AllKeys.Except(SameAsEnglish[culture]))
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
