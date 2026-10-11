using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
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

    private static string DifficultySection(string html, string level)
    {
        var section = Regex.Match(html, $@"<section class=""mb-5"" data-aa-difficulty=""{level}"">(.*?)</section>",
            RegexOptions.Singleline);
        section.Success.Should().BeTrue($"the catalog must include the {level} group");
        return section.Groups[1].Value;
    }

    [Fact]
    public async Task UnknownTrack_IsNotFound()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Exercise?track=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
