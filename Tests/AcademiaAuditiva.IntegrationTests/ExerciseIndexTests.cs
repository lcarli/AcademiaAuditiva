using System.Net;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task UnknownTrack_IsNotFound()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Exercise?track=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
