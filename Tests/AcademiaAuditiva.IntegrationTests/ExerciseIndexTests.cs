using System.Net;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// /Exercise lists the exercises of one training track, Music by default; the selector only
/// shows up when another track has exercises or is asked for, and an empty track says so.
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
    public async Task Music_ListsEveryExercise_WithoutTheSelector(string url)
    {
        var html = await _factory.CreateClient().GetStringAsync(url);

        foreach (var exercise in ExerciseCatalog.All)
            html.Should().Contain($"href=\"/Exercise/{exercise.Name}\"");
        html.Should().NotContain("id=\"trackTabs\"", "no other track has exercises yet")
            .And.NotContain("id=\"trackEmpty\"");
    }

    [Theory]
    [InlineData("en-US", "The Audio track is coming soon", "Music")]
    [InlineData("pt-BR", "A trilha Áudio está chegando", "Música")]
    [InlineData("fr-CA", "Le parcours Audio arrive bientôt", "Musique")]
    public async Task Audio_ShowsTheEmptyState_AndTheWayBackToMusic(string culture, string title, string music)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/Exercise?track=audio&culture={culture}"));

        html.Should().Contain("id=\"trackEmpty\"").And.Contain(title)
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
