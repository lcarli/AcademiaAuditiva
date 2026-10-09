using System.Collections;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.LearningPath;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every exercise belongs to one training track through its category; the current ones, and the
/// learning path, are Music.
/// </summary>
public class TrainingTracksTests
{
    public static TheoryData<string> Cultures => new() { "", "pt-BR", "fr-CA" };

    [Fact]
    public void Tracks_AreMusicAudioAndMix_EachCategoryInOne()
    {
        TrainingTracks.All.Select(t => t.Key).Should().Equal(TrainingTracks.Music, TrainingTracks.Audio, TrainingTracks.Mix);
        TrainingTracks.All.SelectMany(t => t.Categories).Should().OnlyHaveUniqueItems("a category belongs to one track");
    }

    [Fact]
    public void EverySeededCategory_BelongsToATrack()
    {
        SeedData.ExerciseCategories().Should().OnlyContain(c => TrainingTracks.OfCategory(c.Name) != null);
    }

    [Fact]
    public void EveryCurrentExercise_IsMusic()
    {
        ExerciseCatalog.All.Should().OnlyContain(e => e.Track == TrainingTracks.Music);
        LearningPathCatalog.Steps.Should().OnlyContain(s => ExerciseCatalog.TrackOf(s.Exercise) == TrainingTracks.Music,
            "the learning path is the Music path");
    }

    [Fact]
    public void ByTrack_ListsEveryTrack_WithItsCategoriesThatHaveExercises()
    {
        ExerciseCatalog.ByTrack.Select(t => t.Key).Should().Equal(TrainingTracks.All.Select(t => t.Key));
        ExerciseCatalog.ByTrack[0].Categories.Should().Equal(ExerciseCatalog.ByCategory);
        ExerciseCatalog.ByTrack.Skip(1).Should().OnlyContain(t => t.Categories.Count == 0, "no Audio or Mix exercise exists yet");
    }

    [Theory]
    [InlineData("Music", TrainingTracks.Music)]
    [InlineData("audio", TrainingTracks.Audio)]
    [InlineData("MIX", TrainingTracks.Mix)]
    [InlineData("bogus", null)]
    [InlineData(null, null)]
    public void Find_IgnoresCase(string? key, string? expected)
        => (TrainingTracks.Find(key)?.Key).Should().Be(expected);

    [Theory]
    [InlineData("Harmony", true)]
    [InlineData("Level", false)]
    [InlineData("FrequencyEq", false)]
    [InlineData("SomethingElse", true)]
    public void IsMusic_CountsUnlistedCategoriesAsMusic(string category, bool music)
        => TrainingTracks.IsMusic(category).Should().Be(music);

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryTrackAndCategory_HasAText(string culture)
    {
        var resources = Resources(culture);
        var keys = TrainingTracks.All.Select(t => $"TrainingTrack.{t.Key}")
            .Concat(TrainingTracks.All.SelectMany(t => t.Categories).Where(c => c != "Misc").Select(c => $"ExerciseCategory.{c}"))
            .Concat(["TrainingTrack.Label", "TrainingTrack.Empty.Text", "TrainingTrack.Empty.Back"]);

        foreach (var key in keys)
        {
            resources.Should().ContainKey(key);
            resources[key].Should().NotBeNullOrWhiteSpace().And.NotMatchRegex("[{}]", "{0} is shown as is", key);
        }
        resources["TrainingTrack.Empty.Title"].Should().Contain("{0}").And.NotMatchRegex(@"\{[1-9]");
    }

    [Fact]
    public void FrenchTexts_FollowFrenchTypography()
    {
        var resources = Resources("fr-CA");

        foreach (var (key, value) in resources.Where(kv => kv.Key.StartsWith("TrainingTrack.", StringComparison.Ordinal)))
        {
            value.Should().NotMatchRegex(@"[ \S][:!?]", "{0} needs a no-break space before : ! ?", key)
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
