using System.Collections;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Resources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The free practice switch, banner, reveal button and result note, in every culture.
/// Every text is shown as is (the tour texts are covered by <see cref="TutorialResourcesTests"/>).
/// </summary>
public class FreePracticeResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    private static readonly string[] Keys =
    [
        "Exercise.Free.Toggle", "Exercise.Free.Title", "Exercise.Free.Text", "Exercise.Free.Tally",
        "Exercise.Free.Reveal", "Exercise.Free.RevealTitle", "Exercise.Free.ResultNote",
    ];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryText_ExistsWithoutBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in Keys)
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[{}]", "{0} is shown as is", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void FreePracticeTexts_AreOnlyThoseThePageUses(string culture)
    {
        Resources(culture).Keys.Where(key => key.StartsWith("Exercise.Free.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(Keys);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EveryText_IsTranslated(string culture)
    {
        var english = Resources("");
        var translated = Resources(culture);

        foreach (var key in Keys)
        {
            translated[key].Should().NotBe(english[key], "{0} needs a {1} text", key, culture);
        }
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in Keys)
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
