using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Tutorials;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Guided tour texts exist in every culture. tutorial.js fills the step counter's
/// {0} and {1}; every other text is shown as is.
/// </summary>
public class TutorialResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    private const string StepOf = "Tutorial.StepOf";

    private static readonly string[] Labels =
        ["Tutorial.Next", "Tutorial.Back", "Tutorial.Skip", "Tutorial.Done", "Tutorial.Replay"];

    private static IEnumerable<string> StepKeys => TutorialCatalog.All
        .SelectMany(tour => tour.Steps.SelectMany(step => new[] { tour.TitleKey(step), tour.TextKey(step) }));

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryLabelAndStep_HasATextWithoutBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in Labels.Concat(StepKeys))
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[{}]", "{0} is shown as is", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void StepCounter_UsesTheStepAndTheCount(string culture)
    {
        var resources = Resources(culture);

        resources.Should().ContainKey(StepOf);
        var value = resources[StepOf];
        Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Should().BeEquivalentTo(new[] { "{0}", "{1}" });
        Regex.Replace(value, @"\{\d+\}", "").Should().NotMatchRegex("[{}]");
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in Labels.Concat(StepKeys).Append(StepOf))
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
