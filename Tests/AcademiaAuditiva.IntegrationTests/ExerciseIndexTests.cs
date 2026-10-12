using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// /Exercise lists the exercises of one training track, Music by default, and offers every
/// track that currently has exercises.
/// </summary>
public class ExerciseIndexTests : IClassFixture<ExerciseRequestBodyTests.AuthenticatedFactory>
{
    private readonly ExerciseRequestBodyTests.AuthenticatedFactory _factory;

    public ExerciseIndexTests(ExerciseRequestBodyTests.AuthenticatedFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("/Exercise")]
    [InlineData("/Exercise?track=MUSIC")]
    public async Task Music_ListsOnlyMusicExercises_WithTheTrackSelector(string url)
    {
        var html = await _factory.CreateClient().GetStringAsync(url);

        foreach (var exercise in ExerciseCatalog.All.Where(e => e.Track == TrainingTracks.Music))
            html.Should().Contain($"href=\"/Exercise/{exercise.Name}\"");
        html.Should().NotContain("href=\"/Exercise/LevelMatch\"", "Audio has its own catalog tab");
        html.Should().Contain("id=\"trackTabs\"").And.NotContain("id=\"trackEmpty\"");
    }

    [Theory]
    [InlineData("en-US", "Level Match", "Music")]
    [InlineData("pt-BR", "Comparação de nível", "Música")]
    [InlineData("fr-CA", "Comparaison de niveau", "Musique")]
    public async Task Audio_ListsLevelMatch_AndTheWayBackToMusic(string culture, string levelMatch, string music)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/Exercise?track=audio&culture={culture}"));

        html.Should().NotContain("id=\"trackEmpty\"").And.Contain(levelMatch)
            .And.Contain("href=\"/Exercise/LevelMatch\"")
            .And.Contain("id=\"trackTabs\"").And.Contain(music).And.Contain("aria-current=\"page\"");
        html.Should().NotContain("href=\"/Exercise/GuessNote\"", "no Music exercise is listed under Audio");
        html.Should().NotContain("TrainingTrack.", "every text has a resource");
    }

    [Theory]
    [InlineData("en-US", "Beginner / Intermediate / Advanced")]
    [InlineData("pt-BR", "Iniciante / Intermediário / Avançado")]
    [InlineData("fr-CA", "Débutant / Intermédiaire / Avancé")]
    public async Task Audio_ListsEveryExerciseAtEverySupportedLevel_AndSummarizesAllLevelsByType(
        string culture, string summary)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise?track=audio&culture={culture}&ui-culture={culture}"));
        var exercises = ExerciseCatalog.All.Where(e => e.Track == TrainingTracks.Audio).ToArray();
        foreach (var level in new[] { "beginner", "intermediate", "advanced" })
        {
            var section = DifficultySection(html, level);
            var cards = Regex.Matches(section, @"data-aa-exercise=""(\w+)""");
            cards.Select(card => card.Groups[1].Value).Should().BeEquivalentTo(exercises.Select(e => e.Name));
            foreach (var exercise in exercises)
                section.Should().Contain($"href=\"/Exercise/{exercise.Name}?{TechnicalListeningRoundGenerator.LevelFilterFor(exercise.Name)}={level}\"");
        }
        var byType = html[html.IndexOf("id=\"byType\"", StringComparison.Ordinal)..];
        var typeCards = Regex.Matches(byType, @"data-aa-exercise=""(\w+)"">(.*?)</div>", RegexOptions.Singleline);
        typeCards.Select(card => card.Groups[1].Value).Should().BeEquivalentTo(exercises.Select(e => e.Name));
        foreach (Match card in typeCards)
            card.Groups[2].Value.Should().Contain(summary);
        html.Should().NotMatchRegex(@"Exercise\.(Beginner|Intermediate|Advanced)");
    }

    [Fact]
    public async Task Audio_LevelLinksActuallyPreselectTheCorrespondingExerciseFilter()
    {
        var client = _factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Exercise?track=audio"));
        foreach (var level in new[] { "beginner", "intermediate", "advanced" })
        {
            var section = DifficultySection(html, level);
            var links = Regex.Matches(section, @"href=""(/Exercise/\w+\?\w+=\w+)""");
            links.Should().HaveCount(ExerciseCatalog.All.Count(e => e.Track == TrainingTracks.Audio));
            foreach (Match link in links)
            {
                var href = link.Groups[1].Value;
                var filter = href.Split('?')[1].Split('=')[0];
                var page = WebUtility.HtmlDecode(await client.GetStringAsync(href));
                var select = Regex.Match(page, $@"<select id=""{Regex.Escape(filter)}""[^>]*>(.*?)</select>",
                    RegexOptions.Singleline);
                select.Success.Should().BeTrue(href);
                select.Groups[1].Value.Should().Contain($"<option value=\"{level}\" selected=\"selected\">", href);
            }
        }
    }

    [Fact]
    public async Task Music_RetainsItsSingleSeededClassificationAndExistingLinks()
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync("/Exercise"));
        foreach (var level in SeedData.DifficultyLevels().Select((difficulty, index) => (difficulty.Name, Id: index + 1)))
        {
            var section = DifficultySection(html, level.Name.ToLowerInvariant());
            var exercises = ExerciseCatalog.All.Where(e => e.Track == TrainingTracks.Music && e.Difficulty == level.Id);
            Regex.Matches(section, @"data-aa-exercise=""(\w+)""").Select(card => card.Groups[1].Value)
                .Should().BeEquivalentTo(exercises.Select(e => e.Name));
            foreach (var exercise in exercises)
                section.Should().Contain($"href=\"/Exercise/{exercise.Name}\"");
        }
        html.Should().NotContain("Beginner / Intermediate / Advanced");
    }

    [Fact]
    public async Task Audio_AvailableLevelsComeFromItsDeclaredFilterRatherThanAHardcodedList()
    {
        using var factory = new ExerciseRequestBodyTests.AuthenticatedFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SeedData.SeedExercises(db);
            var exercise = db.Exercises.Single(e => e.Name == "LevelMatch");
            var groups = ExerciseFilterPresets.Groups(exercise.FiltersJson);
            groups.Single(group => group.Name == "lmLevel").Options.RemoveAll(option => option.Value == "advanced");
            exercise.FiltersJson = JsonConvert.SerializeObject(groups);
            await db.SaveChangesAsync();
        }
        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/Exercise?track=audio&culture=en-US"));
        DifficultySection(html, "beginner").Should().Contain("data-aa-exercise=\"LevelMatch\"");
        DifficultySection(html, "intermediate").Should().Contain("data-aa-exercise=\"LevelMatch\"");
        DifficultySection(html, "advanced").Should().NotContain("data-aa-exercise=\"LevelMatch\"");
        var byType = html[html.IndexOf("id=\"byType\"", StringComparison.Ordinal)..];
        var card = Regex.Match(byType, @"data-aa-exercise=""LevelMatch"">(.*?)</div>", RegexOptions.Singleline);
        card.Success.Should().BeTrue();
        card.Groups[1].Value.Should().Contain("Beginner / Intermediate").And.NotContain("Advanced");
    }

    [Theory]
    [InlineData(TrainingTracks.Music)]
    [InlineData(TrainingTracks.Audio)]
    public async Task Categories_FollowTrackOrder_AndListEachExerciseOnce(string track)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise?track={track}"));
        var catalog = ExerciseCatalog.ByTrack.Single(t => t.Key == track);
        var pane = TabPane(html, "byCategory");

        GroupNames(pane, "category").Should().Equal(catalog.Categories.Select(c => c.Name));
        CardNames(pane).Should().BeEquivalentTo(catalog.Exercises.Select(e => e.Name))
            .And.OnlyHaveUniqueItems();
        foreach (var category in catalog.Categories)
        {
            var section = CatalogSection(pane, "category", category.Name);
            CardNames(section).Should().BeEquivalentTo(category.Exercises.Select(e => e.Name));
            foreach (var exercise in category.Exercises)
                section.Should().Contain($"href=\"/Exercise/{exercise.Name}\"",
                    "category links preserve the default entry level");
        }
    }

    [Theory]
    [InlineData("en-US", "By Category", "Level and gain", "Stereo and polarity", "Critical listening and diagnosis")]
    [InlineData("pt-BR", "Por categoria", "Nível e ganho", "Estéreo e polaridade", "Escuta crítica e diagnóstico")]
    [InlineData("fr-CA", "Par catégorie", "Niveau et gain", "Stéréo et polarité", "Écoute critique et diagnostic")]
    public async Task Audio_CategoriesAndEveryCardBadge_AreLocalized(
        string culture, string tab, string level, string stereo, string criticalListening)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise?track=audio&culture={culture}&ui-culture={culture}"));

        html.Should().Contain(tab).And.NotContain("Exercise.Tab3.Category")
            .And.NotContain("ExerciseCategory.");
        CatalogSection(html, "category", "Level").Should().Contain(level);
        CatalogSection(html, "category", "StereoPhase").Should().Contain(stereo);
        CatalogSection(html, "category", "CriticalListening").Should().Contain(criticalListening);
        foreach (var exercise in ExerciseCatalog.All.Where(e => e.Track == TrainingTracks.Audio))
        {
            var cards = Regex.Matches(html,
                $@"data-aa-exercise=""{Regex.Escape(exercise.Name)}"">(.*?)</div>",
                RegexOptions.Singleline);
            cards.Should().HaveCount(5, "three level cards, one category card and one type card");
            foreach (Match card in cards)
                card.Groups[1].Value.Should().Contain($"data-aa-category-badge=\"{exercise.Category}\"");
        }
    }

    [Theory]
    [InlineData(TrainingTracks.Music)]
    [InlineData(TrainingTracks.Audio)]
    public async Task CategoriesAndTypes_UseExistingDatabaseLookupsRatherThanSeedOrdinals(string track)
    {
        using var factory = new ExerciseRequestBodyTests.AuthenticatedFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ExerciseCategories.AddRange(SeedData.ExerciseCategories().Select((row, index) =>
                new ExerciseCategory { Id = 1000 - index, Name = row.Name, DisplayName = row.DisplayName }));
            db.ExerciseTypes.AddRange(SeedData.ExerciseTypes().Select((row, index) =>
                new ExerciseType { Id = 2000 + index, Name = row.Name, DisplayName = row.DisplayName }));
            db.DifficultyLevels.AddRange(SeedData.DifficultyLevels().Select((row, index) =>
                new DifficultyLevel { Id = 3000 + index, Name = row.Name, DisplayName = row.DisplayName }));
            await db.SaveChangesAsync();
            SeedData.SeedExercises(db);
        }
        var html = WebUtility.HtmlDecode(await factory.CreateClient()
            .GetStringAsync($"/Exercise?track={track}"));
        var catalog = ExerciseCatalog.ByTrack.Single(t => t.Key == track);
        var categories = TabPane(html, "byCategory");
        GroupNames(categories, "category").Should().Equal(catalog.Categories.Select(c => c.Name));
        foreach (var category in catalog.Categories)
            CardNames(CatalogSection(categories, "category", category.Name))
                .Should().BeEquivalentTo(category.Exercises.Select(e => e.Name));
        CardNames(TabPane(html, "byType")).Should().BeEquivalentTo(catalog.Exercises.Select(e => e.Name));
    }

    [Theory]
    [InlineData(TrainingTracks.Music, "GuessNote")]
    [InlineData(TrainingTracks.Audio, "LevelMatch")]
    public async Task CategoriesAndTypes_OmitEmptySections(string track, string name)
    {
        using var factory = new ExerciseRequestBodyTests.AuthenticatedFactory();
        string category;
        string type;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SeedData.SeedExercises(db);
            var exercise = db.Exercises.Single(e => e.Name == name);
            category = db.ExerciseCategories.Single(c => c.Id == exercise.ExerciseCategoryId).Name;
            type = db.ExerciseTypes.Single(t => t.Id == exercise.ExerciseTypeId).Name;
            db.Exercises.RemoveRange(db.Exercises.Where(e => e.Name != name));
            await db.SaveChangesAsync();
        }
        var html = await factory.CreateClient().GetStringAsync($"/Exercise?track={track}");
        GroupNames(TabPane(html, "byCategory"), "category").Should().Equal(category);
        GroupNames(TabPane(html, "byType"), "type").Should().Equal(type);
        CardNames(CatalogSection(html, "category", category)).Should().Equal(name);
        CardNames(CatalogSection(html, "type", type)).Should().Equal(name);
    }

    [Theory]
    [InlineData("Legacy category", "Legacy category")]
    [InlineData("", "Legacy")]
    [InlineData(" ", "Legacy")]
    public async Task Music_KeepsUnlistedLegacyCategories_AndUsesTheirDisplayNameOrName(
        string displayName, string label)
    {
        using var factory = new ExerciseRequestBodyTests.AuthenticatedFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SeedData.SeedExercises(db);
            var category = new ExerciseCategory { Name = "Legacy", DisplayName = displayName };
            db.ExerciseCategories.Add(category);
            db.Exercises.Single(e => e.Name == "GuessNote").ExerciseCategory = category;
            await db.SaveChangesAsync();
        }
        var html = await factory.CreateClient().GetStringAsync("/Exercise");
        var categories = TabPane(html, "byCategory");
        GroupNames(categories, "category").Last().Should().Be("Legacy");
        var section = CatalogSection(categories, "category", "Legacy");
        CardNames(section).Should().Equal("GuessNote");
        section.Should().Contain($"<h4 class=\"aa-eyebrow mb-3\">{label}</h4>")
            .And.NotContain("ExerciseCategory.Legacy");
        var badge = Regex.Match(section, @"data-aa-category-badge=""Legacy"">(.*?)</span>");
        badge.Success.Should().BeTrue();
        badge.Groups[1].Value.Should().Be(label);
    }

    [Fact]
    public async Task EmptyTrack_KeepsItsEmptyStateWithoutGroupingTabs()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Exercise?track=mix");
        html.Should().Contain("id=\"trackEmpty\"").And.NotContain("id=\"exerciseTabs\"")
            .And.NotContain("data-aa-category").And.NotContain("data-aa-type");
    }

    private static string DifficultySection(string html, string level)
        => CatalogSection(html, "difficulty", level);

    private static string CatalogSection(string html, string grouping, string key)
    {
        var section = Regex.Match(html,
            $@"<section class=""mb-5"" data-aa-{Regex.Escape(grouping)}=""{Regex.Escape(key)}"">(.*?)</section>",
            RegexOptions.Singleline);
        section.Success.Should().BeTrue($"the catalog must include the {grouping} group {key}");
        return section.Groups[1].Value;
    }

    private static string TabPane(string html, string id)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf("<div class=\"tab-pane", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    private static IEnumerable<string> GroupNames(string html, string grouping)
        => Regex.Matches(html, $@"<section[^>]*data-aa-{Regex.Escape(grouping)}=""(\w+)""")
            .Select(match => match.Groups[1].Value);

    private static IEnumerable<string> CardNames(string html)
        => Regex.Matches(html, @"data-aa-exercise=""(\w+)""").Select(match => match.Groups[1].Value);

    [Fact]
    public async Task UnknownTrack_IsNotFound()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Exercise?track=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
