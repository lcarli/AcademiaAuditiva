using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Badge texts, ranks and labels exist in every culture. Views render most of
/// them through IHtmlLocalizer, which runs string.Format even without
/// arguments, so only keys that are always given arguments may hold braces.
/// </summary>
public class GamificationResourcesTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    // Key → number of arguments passed by the views and ExerciseController.
    private static readonly Dictionary<string, int> FormattedKeys = new()
    {
        ["Gamification.Level"] = 1,
        ["Gamification.XpToNext"] = 2,
        ["Gamification.XpGained"] = 1,
        ["Gamification.StreakDaysOne"] = 1,
        ["Gamification.StreakDaysOther"] = 1,
        ["Gamification.BestStreak"] = 1,
        ["Gamification.BadgesCount"] = 2,
        ["Gamification.EarnedOn"] = 1,
        ["Gamification.XpRules"] = 3,
        ["Gamification.LevelUpText"] = 2,
        ["Gamification.BadgesEarnedTitle"] = 1,
    };

    private static readonly string[] PlainKeys =
    [
        "Gamification.Title", "Gamification.Subtitle", "Gamification.ProgressTitle", "Gamification.Streak",
        "Gamification.StreakHint", "Gamification.StreakStart", "Gamification.PracticedToday",
        "Gamification.NoBadgesYet", "Gamification.ViewAll", "Gamification.New", "Gamification.Locked",
        "Gamification.XpRulesTitle", "Gamification.RankHint", "Gamification.SessionRule",
        "Gamification.LevelUpTitle", "Gamification.BadgeEarnedTitle", "Gamification.Continue",
    ];

    private static IEnumerable<string> CatalogKeys => BadgeCatalog.All
        .SelectMany(b => new[] { $"Badge.{b.Key}.Title", $"Badge.{b.Key}.Description" })
        .Concat(BadgeCatalog.Groups.Select(g => $"Gamification.Group.{g}"))
        .Concat(Leveling.Ranks.Select(r => $"Gamification.Rank.{r}"));

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextsWithoutArguments_ExistAndHaveNoBraces(string culture)
    {
        var resources = Resources(culture);

        foreach (var key in PlainKeys.Concat(CatalogKeys))
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
    public void BadgeSeed_HasOneRowPerCatalogBadge()
    {
        SeedData.BadgeSeed().Select(b => b.BadgeKey).Should().BeEquivalentTo(BadgeCatalog.All.Select(b => b.Key));
    }

    private static Dictionary<string, string> Resources(string culture)
    {
        var set = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the {0} resources are deployed", culture);
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
