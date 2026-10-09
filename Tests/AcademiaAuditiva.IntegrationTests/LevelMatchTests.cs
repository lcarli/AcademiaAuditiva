using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio.Processing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

public class LevelMatchTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public LevelMatchTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        _factory.Mixer.ProcessingPlans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("en-US", "Level Match", "Which signal is louder?", "Beginner · 6–12 dB", "A is louder")]
    [InlineData("pt-BR", "Comparação de nível", "Qual sinal está mais alto?", "Iniciante · 6–12 dB", "A está mais alto")]
    [InlineData("fr-CA", "Comparaison de niveau", "Quel signal est le plus fort\u00A0?", "Débutant · 6–12 dB", "A est plus fort")]
    public async Task Page_OffersLocalizedABControlsAndProfiles(
        string culture,
        string title,
        string question,
        string beginner,
        string answerA)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise/LevelMatch?culture={culture}&ui-culture={culture}"));

        html.Should().Contain(title).And.Contain(question).And.Contain(beginner).And.Contain(answerA);
        html.Should().Contain("data-aa-ab-play=\"A\"").And.Contain("data-aa-ab-play=\"B\"");
        html.Should().Contain("id=\"lmLevel\"").And.NotContain("id=\"instrumentButtons\"");
        html.Should().NotMatchRegex(@"Exercise\.LevelMatch|Exercise\.AB\.", "every visible text has a resource");
    }

    [Fact]
    public async Task Round_RendersNamedProcessingPlansWithoutLeakingTheAnswer()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 2, 1, int.MaxValue);

        var play = await PlayAsync(client, exerciseId, "intermediate");

        play.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "clips");
        var clips = play.GetProperty("clips").EnumerateArray().ToArray();
        clips.Select(c => c.GetProperty("key").GetString()).Should().Equal("A", "B");
        clips.Select(c => c.GetProperty("token").GetString()).Should().OnlyContain(t => Regex.IsMatch(t!, "^[0-9a-f]{32}$"));
        play.ToString().Should().NotContainAny(
            "louder", "differenceDb", "source", "gain", "reference", "processed", ".wav");

        var plans = _factory.Mixer.ProcessingPlans.ToArray();
        plans.Should().HaveCount(2);
        plans.Select(p => p.SourceKey).Distinct().Should().ContainSingle();
        var gains = plans.Select(p => ((GainProcessor)p.Processors.Single()).Decibels).ToArray();
        (gains[1] - gains[0]).Should().BeApproximately(4, 0.0001);
        _factory.Mixer.Plans.Should().BeEmpty("technical sources use RenderAsync, not the music mixer");

        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IAudioTokenService>();
        var round = await tokens.GetRoundAsync(
            SignedInWebApplicationFactory.UserId,
            exerciseId,
            play.GetProperty("roundId").GetString()!);
        var expected = JObject.Parse(round!.ExpectedAnswerJson);
        expected["louder"]!.Value<string>().Should().Be("B");
        expected["differenceDb"]!.Value<double>().Should().Be(4);
        JObject.Parse(round.FilterJson!).Should().ContainKeys("lmLevel", "lmDifferenceDb", "lmSourceKind");
    }

    [Fact]
    public async Task IntermediateAnswer_NeedsTheSideAndDifference_AndReturnsUsefulFeedback()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 2, 1, 0);
        var play = await PlayAsync(client, exerciseId, "intermediate");

        var result = await ValidateAsync(client, exerciseId, play, "B|4");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("answer").GetString().Should().Be("B|4");
        var detail = result.GetProperty("detail");
        detail.GetProperty("louder").GetString().Should().Be("B");
        detail.GetProperty("differenceDb").GetDouble().Should().Be(4);
        detail.GetProperty("requiresDifference").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var filters = JObject.Parse(db.ScoreSnapshots
            .Where(s => s.UserId == SignedInWebApplicationFactory.UserId && s.ExerciseId == exerciseId)
            .OrderByDescending(s => s.Timestamp)
            .First().FilterJson!);
        filters["lmLevel"]!.Value<string>().Should().Be("intermediate");
        filters["lmDifferenceDb"]!.Value<string>().Should().Be("4");
        filters["lmSourceKind"]!.Value<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BeginnerAnswer_NeedsOnlyTheLouderSide()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 0, 0, 0);
        var play = await PlayAsync(client, exerciseId, "beginner");

        var result = await ValidateAsync(client, exerciseId, play, "A");

        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("answer").GetString().Should().Be("A");
        result.GetProperty("detail").GetProperty("differenceDb").GetDouble().Should().Be(6);
        result.GetProperty("detail").GetProperty("requiresDifference").GetBoolean().Should().BeFalse();
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "LevelMatch").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    private static async Task<JsonElement> PlayAsync(HttpClient client, int exerciseId, string level) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync(
            "/Exercise/RequestPlay",
            new { exerciseId, filters = new Dictionary<string, string> { ["lmLevel"] = level } }));

    private static async Task<JsonElement> ValidateAsync(
        HttpClient client,
        int exerciseId,
        JsonElement play,
        string userGuess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync(
            "/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));
}
