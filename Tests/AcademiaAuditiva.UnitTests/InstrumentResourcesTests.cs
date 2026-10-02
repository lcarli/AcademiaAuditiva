using System.Collections;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The instrument buttons of the exercise filters and the footer link to the credits,
/// in every culture.
/// </summary>
public class InstrumentResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    private static readonly string[] InstrumentKeys = [.. Instrument.All.Select(i => i.LabelKey)];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryText_ExistsWithoutBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in InstrumentKeys.Append("Layout.Credits"))
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[{}]", "{0} is shown as is", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void InstrumentTexts_AreThoseOfTheInstruments(string culture)
    {
        Resources(culture).Keys.Where(key => key.StartsWith("Instrument.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(InstrumentKeys);
    }

    [Theory]
    [InlineData("pt-BR", "Instrument.Guitar")]
    [InlineData("pt-BR", "Instrument.Violin")]
    [InlineData("pt-BR", "Layout.Credits")]
    [InlineData("fr-CA", "Instrument.Guitar")]
    [InlineData("fr-CA", "Instrument.Violin")]
    [InlineData("fr-CA", "Layout.Credits")]
    public void Text_IsTranslated(string culture, string key)
    {
        Resources(culture)[key].Should().NotBe(Resources("")[key]);
    }

    private static Dictionary<string, string> Resources(string culture)
    {
        var set = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the {0} resources are deployed", culture);
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
