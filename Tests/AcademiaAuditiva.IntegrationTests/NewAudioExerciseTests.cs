using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

public class NewAudioExerciseTests : IDisposable
{
    private readonly ExploreWebApplicationFactory _factory = new();

    public NewAudioExerciseTests()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    public void Dispose() => _factory.Dispose();

    [Theory]
    [MemberData(nameof(ExpandedPages))]
    [InlineData("BoostOrCut", "en-US", "Boost or Cut")]
    [InlineData("BoostOrCut", "pt-BR", "Realce ou corte")]
    [InlineData("BoostOrCut", "fr-CA", "Accentuation ou atténuation")]
    [InlineData("GuessQ", "en-US", "Guess the Q")]
    [InlineData("GuessQ", "pt-BR", "Identificação de Q")]
    [InlineData("GuessQ", "fr-CA", "Identification du Q")]
    [InlineData("Compression", "en-US", "Compression")]
    [InlineData("Compression", "pt-BR", "Compressão")]
    [InlineData("Compression", "fr-CA", "Compression")]
    [InlineData("EqMatching", "en-US", "EQ Matching")]
    [InlineData("EqMatching", "pt-BR", "Correspondência de EQ")]
    [InlineData("EqMatching", "fr-CA", "Correspondance d’égalisation")]
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
        if (exercise == "EqMatching")
            html.Should().Contain("id=\"eqFrequency\"").And.Contain("id=\"eqGain\"").And.Contain("id=\"eqQ\"")
                .And.Contain("id=\"previewEq\"").And.Contain("aa-channel-strip")
                .And.Contain("id=\"eqResponseCurve\"").And.Contain("aria-describedby=\"eqKnobHelp\"")
                .And.NotContain("Exercise.EqMatching.");
        if (AudioComparisonCatalog.Expansion.Any(definition => definition.Name == exercise))
        {
            html.Should().NotContain($"Exercise.{exercise}.").And.NotContain("LearningPath.Unit.");
            foreach (var answer in AudioComparisonCatalog.Find(exercise)!.Answers)
                html.Should().Contain($"value=\"{answer}\"");
        }
    }

    [Theory]
    [InlineData("beginner")]
    [InlineData("intermediate")]
    [InlineData("advanced")]
    public async Task EqMatchingRound_HidesTargetAndScoresAnExactCurveOnce(string level)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(exercise => exercise.Name == "EqMatching").ExerciseId;
        var before = db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId);
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());
        _factory.AudioRandom.Use(0, 0, 0, 0, 0, 0, 0);
        _factory.Mixer.ProcessingPlans.Clear();

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId, filters = new Dictionary<string, string> { ["eqmLevel"] = level },
        }));
        play.EnumerateObject().Select(property => property.Name).Should().Equal("roundId", "clips");
        play.GetProperty("clips").GetArrayLength().Should().Be(2);
        play.ToString().Should().NotContainAny("frequency", "gain", "source", ".wav");
        var roundId = play.GetProperty("roundId").GetString()!;
        var stored = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
            .GetRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId, roundId);
        var expected = JObject.Parse(stored!.ExpectedAnswerJson);
        var answer = expected["answer"]!.Value<string>()!;
        _factory.Mixer.ProcessingPlans.Should().HaveCount(2)
            .And.OnlyContain(plan => plan.Processors.Last() is LoudnessMatchProcessor);

        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("detail").GetProperty("score").GetDouble().Should().Be(1);
        var repeat = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        repeat.GetProperty("success").GetBoolean().Should().BeFalse();
        db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId).Should().Be(before + 1);
    }

    [Theory]
    [MemberData(nameof(ExpandedRounds))]
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
        clips.Should().HaveCount(name == "MixProblem" ? 7 : 2);
        foreach (var clip in clips)
            clip.EnumerateObject().Select(property => property.Name).Should().Equal("key", "token");
        play.ToString().Should().NotContainAny("source", "ratio", "direction", "region", "threshold", "frequency", "width", ".wav");
        var plans = _factory.Mixer.ProcessingPlans.ToArray();
        plans.Should().HaveCount(name == "MixProblem" ? 7 : 2);
        plans.Select(plan => plan.SourceKey).Distinct().Should().ContainSingle();
        plans.Should().OnlyContain(plan => plan.Processors.Last() is LoudnessMatchProcessor);
        var roundId = play.GetProperty("roundId").GetString()!;
        var reveal = await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId });
        reveal.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        if (AudioComparisonCatalog.Expansion.Any(definition => definition.Name == name))
        {
            filters["audioAnswer"]!.Value<string>().Should().Be(answer);
            filters["audioParameters"]!.Value<string>().Should().Be(expected["parameters"]!.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    public static IEnumerable<object[]> ExpandedPages =>
        AudioComparisonCatalog.Expansion.SelectMany(definition => new[] { "en-US", "pt-BR", "fr-CA" }
            .Select(culture => new object[] { definition.Name, culture,
                new ResourceManager(typeof(SharedResources)).GetString(definition.Name, CultureInfo.GetCultureInfo(culture))! }));

    public static IEnumerable<object[]> ExpandedRounds =>
        AudioComparisonCatalog.Expansion.SelectMany(definition => new[] { "beginner", "intermediate", "advanced" }
            .Select(level => new object[] { definition.Name, level }));

    [Theory]
    [MemberData(nameof(ExpandedRounds))]
    public async Task ExpandedFreePractice_RevealsAnswer_WithoutScoresOrRewards(string name, string level)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(exercise => exercise.Name == name).ExerciseId;
        var before = db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId);
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());
        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId, free = true,
            filters = new Dictionary<string, string> { [TechnicalListeningRoundGenerator.LevelFilterFor(name)] = level },
        }));
        play.EnumerateObject().Select(property => property.Name).Should().Equal("roundId", "clips");
        var roundId = play.GetProperty("roundId").GetString()!;
        var revealed = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer", new
        {
            exerciseId, roundId,
        }));
        revealed.GetProperty("success").GetBoolean().Should().BeTrue();
        var answer = revealed.GetProperty("answer").GetString()!;
        AudioComparisonCatalog.Find(name)!.Answers.Should().Contain(answer);
        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", new
        {
            exerciseId, roundId, userGuess = answer,
        }));
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("free").GetBoolean().Should().BeTrue();
        result.TryGetProperty("rewards", out _).Should().BeFalse();
        result.TryGetProperty("newCorrectCount", out _).Should().BeFalse();
        db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId).Should().Be(before);
    }
}
