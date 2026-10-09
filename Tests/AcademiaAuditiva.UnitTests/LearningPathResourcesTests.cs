using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.LearningPath;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Learning path texts exist in every culture. Views render them through
/// IHtmlLocalizer, which runs string.Format even without arguments, so only keys
/// that are always given arguments may hold braces.
/// </summary>
public class LearningPathResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Key → number of arguments passed by the views and ExerciseController.
    private static readonly Dictionary<string, int> FormattedKeys = new()
    {
        ["LearningPath.StepsCount"] = 2,
        ["LearningPath.UnitNumber"] = 1,
        ["LearningPath.StepNumber"] = 1,
        ["LearningPath.StepOf"] = 2,
        ["LearningPath.Goal"] = 2,
        ["LearningPath.Progress"] = 2,
        ["LearningPath.StepCompleteText"] = 2,
        ["LearningPath.NextStep"] = 1,
    };

    private static readonly string[] PlainKeys =
    [
        "Header.LearningPath", "LearningPath.Title", "LearningPath.Subtitle", "LearningPath.Start",
        "LearningPath.Continue", "LearningPath.ViewPath", "LearningPath.LockedHint", "LearningPath.Done",
        "LearningPath.DoneText", "LearningPath.Empty", "LearningPath.StepComplete.Title",
        "LearningPath.UnitComplete.Title", "LearningPath.PathComplete.Title", "LearningPath.GoToNext",
        "LearningPath.Audio.Title", "LearningPath.Audio.Subtitle",
    ];

    private static IEnumerable<string> CatalogKeys => LearningPathCatalog.AllUnits
        .SelectMany(u => new[] { $"LearningPath.Unit.{u.Key}.Title", $"LearningPath.Unit.{u.Key}.Description" })
        .Concat(Enum.GetNames<StepState>().Select(state => $"LearningPath.State.{state}"));

    // Step titles and subtitles are the exercise's own texts.
    private static IEnumerable<string> ExerciseKeys => LearningPathCatalog.ByTrack.Values
        .SelectMany(units => units).SelectMany(unit => unit.Steps)
        .SelectMany(s => new[] { s.Exercise, $"Exercise.{s.Exercise}.Subtitle" });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithoutArguments_ExistAndHaveNoBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in PlainKeys.Concat(CatalogKeys).Concat(ExerciseKeys))
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

        foreach (var key in PlainKeys.Concat(CatalogKeys).Concat(FormattedKeys.Keys))
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
