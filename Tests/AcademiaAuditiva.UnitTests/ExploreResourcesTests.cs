using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The Explore page names every choice <see cref="ExploreSoundBuilder"/> offers, in
/// every culture. explore.js fills the staff label's {0}; every other text is shown as is.
/// </summary>
public class ExploreResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    private const string StaffLabel = "Explore.StaffLabel";

    private static readonly string[] PageKeys =
    [
        "Explore.Eyebrow", "Explore.Title", "Explore.Lead", "Explore.Tabs",
        "Explore.Tab.Note", "Explore.Tab.Interval", "Explore.Tab.Chord", "Explore.Tab.Scale",
        "Explore.Root.Note", "Explore.Root.Interval", "Explore.Root.Chord", "Explore.Root.Scale",
        "Explore.Interval", "Explore.Direction.Harmonic", "Explore.Inversion",
        "Explore.Style", "Explore.Style.Block", "Explore.Style.Arpeggio",
        "Explore.Scale", "Explore.ScaleGroup.Scales", "Explore.ScaleGroup.Modes",
        "Explore.NotesLabel", "Explore.Keyboard", "Explore.KeyboardHint.Note", "Explore.KeyboardHint.Root",
        "Explore.Error", "Explore.RateLimited",
    ];

    // Texts the page shares with the menu and the exercises.
    private static readonly string[] SharedKeys =
    [
        "Header.Explore",
        "Exercise.Octave", "Exercise.OctaveDown", "Exercise.OctaveUp",
        "Exercise.Direction", "Exercise.Direction.Asc", "Exercise.Direction.Desc",
        "Exercise.Quality.ChordGroup", "Exercise.Audio.Play",
        "Exercise.InversionRoot", "Exercise.InversionFirst", "Exercise.InversionSecond", "Exercise.InversionThird",
    ];

    private static IEnumerable<string> ChoiceKeys =>
        ExploreSoundBuilder.Roots.Select(root => root.Length == 1 ? $"Exercise.Note.{root}" : $"Exercise.Note.{root[0]}Sharp")
            .Concat(ExploreSoundBuilder.IntervalCodes.Select(code => $"Exercise.Interval.{code}"))
            .Concat(ExploreSoundBuilder.ChordQualities.Select(quality => $"Exercise.ChordQuality.{Pascal(quality.Code)}"))
            .Concat(ExploreSoundBuilder.ScaleNames.Select(scale => $"Exercise.ScaleType{Pascal(scale)}"))
            .Concat(ExploreSoundBuilder.ModeNames.Select(mode => $"Exercise.Mode{Pascal(mode)}"));

    private static IEnumerable<string> PlainKeys => PageKeys.Concat(SharedKeys).Concat(ChoiceKeys);

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryTextThePageShows_ExistsWithoutBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in PlainKeys)
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace("{0} needs a text", key)
                .And.NotMatchRegex("[{}]", "{0} is shown as is", key);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void StaffLabel_NamesTheSound(string culture)
    {
        var resources = Resources(culture);

        resources.Should().ContainKey(StaffLabel);
        var value = resources[StaffLabel];
        Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Should().Equal("{0}");
        value.Replace("{0}", "").Should().NotMatchRegex("[{}]");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ExploreTexts_AreOnlyThoseThePageUses(string culture)
    {
        Resources(culture).Keys.Where(key => key.StartsWith("Explore.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(PageKeys.Append(StaffLabel));
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var key in PlainKeys.Append(StaffLabel))
        {
            resources[key].Should().NotMatchRegex(@"[ \S][:!?]", "{0} needs a no-break space before : ! ?", key)
                .And.NotContain("'", "{0} uses typographic apostrophes", key);
        }
    }

    private static string Pascal(string code) => char.ToUpperInvariant(code[0]) + code[1..];

    private static Dictionary<string, string> Resources(string culture)
    {
        var set = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the {0} resources are deployed", culture);
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
