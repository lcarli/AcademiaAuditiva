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

public class StereoPositionTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public StereoPositionTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        _factory.Mixer.ProcessingPlans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("en-US", "Stereo Position", "Where is the sound in the stereo field?", "Left", "mono output")]
    [InlineData("pt-BR", "Posição estéreo", "Onde o som está no campo estéreo?", "Esquerda", "saída mono")]
    [InlineData("fr-CA", "Position stéréo", "Où se trouve le son dans le champ stéréo\u00A0?", "Gauche", "sortie mono")]
    public async Task Page_OffersLocalizedSingleControlPositionsAndDeviceNote(
        string culture,
        string title,
        string question,
        string left,
        string deviceNote)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise/StereoPosition?culture={culture}&ui-culture={culture}"));

        html.Should().Contain(title).And.Contain(question).And.Contain(left).And.Contain(deviceNote);
        html.Should().Contain("data-aa-ab-play=\"A\"").And.NotContain("data-aa-ab-play=\"B\"");
        html.Should().Contain("id=\"spLevel\"").And.NotContain("id=\"instrumentButtons\"");
        html.Should().Contain("value=\"L75\"").And.Contain("value=\"R50\"").And.Contain("value=\"C\"");
        html.Should().NotMatchRegex(@"Exercise\.StereoPosition|Exercise\.AB\.", "every visible text has a resource");
    }

    [Fact]
    public async Task Round_RendersOnePannedClipWithoutLeakingThePosition()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 4);

        var play = await PlayAsync(client, exerciseId, "intermediate");

        play.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "clips");
        var clip = play.GetProperty("clips").EnumerateArray().Should().ContainSingle().Subject;
        clip.GetProperty("key").GetString().Should().Be("A");
        Regex.IsMatch(clip.GetProperty("token").GetString()!, "^[0-9a-f]{32}$").Should().BeTrue();
        play.ToString().Should().NotContainAny("position", "pan", "left", "right", "source", ".wav");

        var plan = _factory.Mixer.ProcessingPlans.Should().ContainSingle().Subject;
        ((PanProcessor)plan.Processors.Single()).Position.Should().Be(0.75);
        _factory.Mixer.Plans.Should().BeEmpty("technical sources use RenderAsync, not the music mixer");

        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IAudioTokenService>();
        var round = await tokens.GetRoundAsync(
            SignedInWebApplicationFactory.UserId,
            exerciseId,
            play.GetProperty("roundId").GetString()!);
        var expected = JObject.Parse(round!.ExpectedAnswerJson);
        expected["answer"]!.Value<string>().Should().Be("R75");
        expected["position"]!.Value<double>().Should().Be(0.75);
        JObject.Parse(round.FilterJson!).Should().ContainKeys("spLevel", "spPosition", "spSourceKind");
    }

    [Fact]
    public async Task Answer_IsValidatedRecordedWithItsPosition_AndGivesFeedbackDetail()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 0);
        var play = await PlayAsync(client, exerciseId, "advanced");

        var wrong = await ValidateAsync(client, exerciseId, play, "R75");
        wrong.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        wrong.GetProperty("answer").GetString().Should().Be("L75");

        _factory.AudioRandom.Use(0, 0);
        play = await PlayAsync(client, exerciseId, "advanced");
        var result = await ValidateAsync(client, exerciseId, play, "L75");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        var detail = result.GetProperty("detail");
        detail.GetProperty("answer").GetString().Should().Be("L75");
        detail.GetProperty("position").GetDouble().Should().Be(-0.75);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var filters = JObject.Parse(db.ScoreSnapshots
            .Where(s => s.UserId == SignedInWebApplicationFactory.UserId && s.ExerciseId == exerciseId)
            .OrderByDescending(s => s.Timestamp)
            .First().FilterJson!);
        filters["spLevel"]!.Value<string>().Should().Be("advanced");
        filters["spPosition"]!.Value<string>().Should().Be("-0.75");
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "StereoPosition").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    private static async Task<JsonElement> PlayAsync(HttpClient client, int exerciseId, string level) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync(
            "/Exercise/RequestPlay",
            new { exerciseId, filters = new Dictionary<string, string> { ["spLevel"] = level } }));

    private static async Task<JsonElement> ValidateAsync(
        HttpClient client,
        int exerciseId,
        JsonElement play,
        string userGuess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync(
            "/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));
}
