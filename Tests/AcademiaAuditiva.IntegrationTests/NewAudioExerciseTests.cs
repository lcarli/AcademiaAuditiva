using System.Net;
using System.Net.Http.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

public class NewAudioExerciseTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public NewAudioExerciseTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("BoostOrCut", "en-US", "Boost or Cut")]
    [InlineData("BoostOrCut", "pt-BR", "Realce ou corte")]
    [InlineData("BoostOrCut", "fr-CA", "Accentuation ou atténuation")]
    [InlineData("GuessQ", "en-US", "Guess the Q")]
    [InlineData("GuessQ", "pt-BR", "Identificação de Q")]
    [InlineData("GuessQ", "fr-CA", "Identification du Q")]
    [InlineData("Compression", "en-US", "Compression")]
    [InlineData("Compression", "pt-BR", "Compressão")]
    [InlineData("Compression", "fr-CA", "Compression")]
    public async Task Pages_LocalizeControlsInstructionsAndAllAnswers(string exercise, string culture, string title)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Exercise/{exercise}?culture={culture}&ui-culture={culture}"));
        html.Should().Contain(title).And.Contain($"id=\"{TechnicalListeningRoundGenerator.LevelFilterFor(exercise)}\"")
            .And.Contain("data-aa-ab-play=\"A\"").And.Contain("data-aa-ab-play=\"B\"")
            .And.NotContain("id=\"instrumentButtons\"").And.NotContain("id=\"rangeFilter\"");
        html.Should().NotMatchRegex(@"Exercise\.(BoostOrCut|GuessQ|Compression|AudioComparison|AB)\.");
        if (exercise == "BoostOrCut") html.Should().Contain("value=\"cut|bass\"").And.Contain("value=\"boost|air\"");
        if (exercise == "GuessQ") html.Should().Contain("value=\"broad\"").And.Contain("value=\"narrow\"");
    }

    [Theory]
    [InlineData("BoostOrCut", "beginner")]
    [InlineData("BoostOrCut", "intermediate")]
    [InlineData("BoostOrCut", "advanced")]
    [InlineData("GuessQ", "beginner")]
    [InlineData("GuessQ", "intermediate")]
    [InlineData("GuessQ", "advanced")]
    [InlineData("Compression", "beginner")]
    [InlineData("Compression", "intermediate")]
    [InlineData("Compression", "advanced")]
    public async Task Rounds_HideParameters_ScoreOnce_AndStoreOnlyTrustedMetadata(string name, string level)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(exercise => exercise.Name == name).ExerciseId;
        var before = db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId);
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());
        _factory.AudioRandom.Use(0, 0, 0, 0);
        _factory.Mixer.ProcessingPlans.Clear();
        var filterKey = TechnicalListeningRoundGenerator.LevelFilterFor(name);
        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId, filters = new Dictionary<string, string>
            {
                [filterKey] = level, ["bcDirection"] = "fake", ["gqQ"] = "fake", ["cpRatio"] = "fake",
            },
        }));
        play.EnumerateObject().Select(property => property.Name).Should().Equal("roundId", "clips");
        var clips = play.GetProperty("clips").EnumerateArray().ToArray();
        clips.Should().HaveCount(2);
        foreach (var clip in clips)
            clip.EnumerateObject().Select(property => property.Name).Should().Equal("key", "token");
        play.ToString().Should().NotContainAny("source", "ratio", "direction", "region", "threshold", "frequency", "width", ".wav");
        var plans = _factory.Mixer.ProcessingPlans.ToArray();
        plans.Should().HaveCount(2);
        plans.Select(plan => plan.SourceKey).Distinct().Should().ContainSingle();
        plans.Should().OnlyContain(plan => plan.Processors.Last() is LoudnessMatchProcessor);
        var roundId = play.GetProperty("roundId").GetString()!;
        var stored = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
            .GetRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId, roundId);
        var expected = JObject.Parse(stored!.ExpectedAnswerJson);
        var answer = expected["answer"]!.Value<string>()!;
        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("detail").EnumerateObject().Should().NotBeEmpty();
        var repeat = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        repeat.GetProperty("success").GetBoolean().Should().BeFalse();
        db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId).Should().Be(before + 1);
        var snapshot = db.ScoreSnapshots.Where(snapshot => snapshot.ExerciseId == exerciseId).OrderByDescending(snapshot => snapshot.Id).First();
        var filters = JObject.Parse(snapshot.FilterJson!);
        filters[filterKey]!.Value<string>().Should().Be(level);
        filters.ToString().Should().NotContain("fake");
    }
}
