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

public class GuessFrequencyTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public GuessFrequencyTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        _factory.Mixer.ProcessingPlans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("en-US", "Guess Frequency", "Which center frequency was boosted?", "matched loudness")]
    [InlineData("pt-BR", "Identificação de frequência", "Qual frequência central foi realçada?", "loudness equivalente")]
    [InlineData("fr-CA", "Identification de fréquence", "Quelle fréquence centrale a été accentuée\u00A0?", "sonie équivalente")]
    public async Task Page_OffersLocalizedABControlsAndAllProfileOptions(
        string culture, string title, string question, string instructions)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise/GuessFrequency?culture={culture}&ui-culture={culture}"));

        html.Should().Contain(title).And.Contain(question).And.Contain(instructions);
        html.Should().Contain("data-aa-ab-play=\"A\"").And.Contain("data-aa-ab-play=\"B\"");
        html.Should().Contain("id=\"gfLevel\"").And.NotContain("id=\"instrumentButtons\"");
        html.Should().Contain("value=\"100\"").And.Contain("value=\"10000\"").And.Contain("value=\"630\"");
        html.Should().NotMatchRegex(@"Exercise\.GuessFrequency|Exercise\.AB\.");
    }

    [Fact]
    public async Task Round_OnlyExposesOpaqueClips_WhileKeepingEQAndMatchedTargetOnTheServer()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(3, 0, 1, 0);

        var play = await PlayAsync(client, exerciseId, "intermediate");

        play.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "clips");
        var clips = play.GetProperty("clips").EnumerateArray().ToArray();
        clips.Select(c => c.GetProperty("key").GetString()).Should().Equal("A", "B");
        clips.Should().OnlyContain(c => c.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "key", "token" }));
        clips.Select(c => c.GetProperty("token").GetString()).Should().OnlyContain(t => Regex.IsMatch(t!, "^[0-9a-f]{32}$"));
        play.ToString().Should().NotContainAny("frequency", "boosted", "region", "source", "gain", "lufs", ".wav");

        var plans = _factory.Mixer.ProcessingPlans.ToArray();
        plans.Should().HaveCount(2);
        plans.Select(p => p.SourceKey).Distinct().Should().ContainSingle();
        plans[0].Processors.Should().Equal(new LoudnessMatchProcessor(-26));
        plans[1].Processors.Should().Equal(new PeakingEqProcessor(1000, 6, 1), new LoudnessMatchProcessor(-26));
        _factory.Mixer.Plans.Should().BeEmpty();

        using var scope = _factory.Services.CreateScope();
        var round = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>().GetRoundAsync(
            SignedInWebApplicationFactory.UserId, exerciseId, play.GetProperty("roundId").GetString()!);
        var expected = JObject.Parse(round!.ExpectedAnswerJson);
        expected["frequencyHz"]!.Value<int>().Should().Be(1000);
        expected["boostedClip"]!.Value<string>().Should().Be("B");
        JObject.Parse(round.FilterJson!).Should().ContainKeys("gfLevel", "gfFrequencyHz", "gfSourceKind");
    }

    [Fact]
    public async Task Answer_IsScoredOnce_StoresTrustedMetadata_AndOnlyThenRevealsEducationalFeedback()
    {
        var (exerciseId, client) = await StartAsync();
        using var beforeScope = _factory.Services.CreateScope();
        var beforeCount = beforeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .ScoreSnapshots.Count(s => s.UserId == SignedInWebApplicationFactory.UserId && s.ExerciseId == exerciseId);
        _factory.AudioRandom.Use(3, 0, 1, 0);
        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId,
            filters = new Dictionary<string, string>
            {
                ["gfLevel"] = "advanced", ["gfFrequencyHz"] = "100", ["gfSourceKind"] = "fake",
            },
        }));

        var result = await ValidateAsync(client, exerciseId, play, "1000");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("answer").GetString().Should().Be("1000");
        var detail = result.GetProperty("detail");
        detail.GetProperty("frequencyHz").GetInt32().Should().Be(1000);
        detail.GetProperty("boostedClip").GetString().Should().Be("B");
        detail.GetProperty("region").GetString().Should().Be("mids");
        detail.GetProperty("sourceKind").GetString().Should().Be("keys");
        var repeated = await ValidateAsync(client, exerciseId, play, "1000");
        repeated.GetProperty("success").GetBoolean().Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshots = db.ScoreSnapshots.Where(s =>
            s.UserId == SignedInWebApplicationFactory.UserId && s.ExerciseId == exerciseId);
        snapshots.Count().Should().Be(beforeCount + 1);
        var snapshot = snapshots.OrderByDescending(s => s.Id).First();
        var filters = JObject.Parse(snapshot.FilterJson!);
        filters["gfLevel"]!.Value<string>().Should().Be("advanced");
        filters["gfFrequencyHz"]!.Value<string>().Should().Be("1000");
        filters["gfSourceKind"]!.Value<string>().Should().Be("keys");
    }

    [Fact]
    public async Task WrongFrequency_IsRecordedAsWrong_AndReturnsTheActualFrequency()
    {
        var (exerciseId, client) = await StartAsync();
        _factory.AudioRandom.Use(0, 0, 0, 0);
        var play = await PlayAsync(client, exerciseId, "beginner");

        var result = await ValidateAsync(client, exerciseId, play, "500");

        result.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        result.GetProperty("detail").GetProperty("frequencyHz").GetInt32().Should().Be(100);
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Exercises.Single(e => e.Name == "GuessFrequency").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (id, client);
    }

    private static async Task<JsonElement> PlayAsync(HttpClient client, int id, string level) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay",
            new { exerciseId = id, filters = new Dictionary<string, string> { ["gfLevel"] = level } }));

    private static async Task<JsonElement> ValidateAsync(HttpClient client, int id, JsonElement play, string guess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId = id, roundId = play.GetProperty("roundId").GetString(), userGuess = guess }));
}
