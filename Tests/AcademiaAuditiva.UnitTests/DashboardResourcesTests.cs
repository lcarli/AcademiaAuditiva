using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The student dashboard's texts exist in every culture. The view renders them through
/// IHtmlLocalizer, which runs string.Format even without arguments, so only keys that
/// are always given arguments may hold braces.
/// </summary>
public class DashboardResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Key → number of arguments passed by the dashboard view and UserReportService.
    private static readonly Dictionary<string, int> FormattedKeys = new()
    {
        ["Dashboard.MostMissedHint"] = 1,
        ["Report.ReviewExercise"] = 1,
    };

    private static readonly string[] PlainKeys =
    [
        "Dashboard.TotalAnswers", "Dashboard.ChartDailyAccuracy", "Report.KeepPracticing",
        "Report.KeepCurrentLevel", "Report.IncreaseDifficulty",
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

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in PlainKeys.Concat(FormattedKeys.Keys))
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
