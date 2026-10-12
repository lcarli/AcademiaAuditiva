using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Gamification;
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
        if (exercise == "BoostOrCut")
        {
            html.Should().Contain("data-bc-direction=\"boost\"").And.Contain("data-bc-direction=\"cut\"")
                .And.Contain("class=\"aa-bc-directions\" data-tour=\"answer\"")
                .And.Contain("class=\"aa-bc-bands\" data-tour=\"answer\"")
                .And.Contain("value=\"sub\"").And.Contain("value=\"air\"")
                .And.Contain("125 Hz").And.Contain("8 kHz");
            foreach (var answer in AudioComparisonCatalog.Find(exercise)!.Answers)
                html.Should().Contain($"value=\"{answer.Split('|')[1]}\"");
        }
        if (exercise == "GuessQ") html.Should().Contain("value=\"broad\"").And.Contain("value=\"narrow\"");
        if (exercise == "EqMatching")
            html.Should().Contain("id=\"eqFrequency\"").And.Contain("id=\"eqGain\"").And.Contain("id=\"eqQ\"")
                .And.Contain("id=\"previewEq\"").And.Contain("aa-channel-strip")
                .And.Contain("id=\"eqResponseCurve\"").And.Contain("aria-describedby=\"eqKnobHelp\"")
                .And.Contain("class=\"aa-eq-controls\" data-tour=\"answer\"")
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
            exerciseId, filters = new Dictionary<string, string> { ["eqmLevel"] = level, ["eqmScoringVersion"] = "1" },
        }));
        play.EnumerateObject().Select(property => property.Name).Should().Equal("roundId", "clips");
        play.GetProperty("clips").GetArrayLength().Should().Be(2);
        play.ToString().Should().NotContainAny("frequency", "gain", "source", ".wav");
        var roundId = play.GetProperty("roundId").GetString()!;
        var stored = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
            .GetRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId, roundId);
        var expected = JObject.Parse(stored!.ExpectedAnswerJson);
        expected["scoringVersion"]!.Value<int>().Should().Be(2);
        var answer = expected["answer"]!.Value<string>()!;
        _factory.Mixer.ProcessingPlans.Should().HaveCount(2)
            .And.OnlyContain(plan => plan.Processors.Last() is LoudnessMatchProcessor);

        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        result.GetProperty("detail").GetProperty("score").GetDouble().Should().Be(1);
        result.GetProperty("detail").GetProperty("scoringVersion").GetInt32().Should().Be(2);
        result.GetProperty("detail").GetProperty("failedCriteria").GetArrayLength().Should().Be(0);
        var repeat = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        repeat.GetProperty("success").GetBoolean().Should().BeFalse();
        db.ScoreSnapshots.Count(snapshot => snapshot.ExerciseId == exerciseId).Should().Be(before + 1);
        var snapshot = db.ScoreSnapshots.Single(snapshot => snapshot.ExerciseId == exerciseId);
        JObject.Parse(snapshot.FilterJson!)["eqmScoringVersion"]!.Value<string>().Should().Be("2");
    }

    [Theory]
    [InlineData("beginner", false)]
    [InlineData("intermediate", false)]
    [InlineData("advanced", false)]
    [InlineData("beginner", true)]
    public async Task EqMatchingPilot_HighPartialCreditIsNotCorrect_AndFreePracticeStillStoresNothing(
        string level, bool free)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "EqMatching").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());
        _factory.AudioRandom.Use(0, 0, 0, 0, 0, 0, 0);
        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId, free, filters = new Dictionary<string, string> { ["eqmLevel"] = level, ["eqmScoringVersion"] = "1" },
        }));
        var roundId = play.GetProperty("roundId").GetString()!;
        var stored = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
            .GetRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId, roundId);
        var target = JObject.Parse(stored!.ExpectedAnswerJson);
        var guess = FormattableString.Invariant($"{target["frequencyHz"]!.Value<double>():R}|0|{target["q"]!.Value<double>():R}");
        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = guess }));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        var detail = result.GetProperty("detail");
        detail.GetProperty("score").GetDouble().Should().BeApproximately(0.9, 1e-12);
        detail.GetProperty("scoringVersion").GetInt32().Should().Be(2);
        detail.GetProperty("failedCriteria").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("direction", "gain");
        if (free)
        {
            result.GetProperty("free").GetBoolean().Should().BeTrue();
            result.TryGetProperty("rewards", out _).Should().BeFalse();
            db.ScoreSnapshots.Should().BeEmpty();
            db.Scores.Should().BeEmpty();
            db.ScoreAggregates.Should().BeEmpty();
        }
        else
        {
            var snapshot = db.ScoreSnapshots.Single(s => s.ExerciseId == exerciseId);
            snapshot.IsCorrect.Should().BeFalse();
            var filters = ExerciseFilterPresets.Parse(snapshot.FilterJson);
            filters["eqmScoringVersion"].Should().Be("2");
            filters["eqmLevel"].Should().Be(level);
            result.GetProperty("newCorrectCount").GetInt32().Should().Be(0);
            result.GetProperty("newErrorCount").GetInt32().Should().Be(1);
            var rewards = result.GetProperty("rewards");
            rewards.GetProperty("xpGained").GetInt32().Should().Be(
                Leveling.XpPerWrongAnswer + rewards.GetProperty("badges").GetArrayLength() * Leveling.XpPerBadge);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqMatching_OldPendingRoundAndHistoricalCreditRemainLegacy_WhenNewRoundsUsePilot(bool explicitVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "EqMatching").ExerciseId;
        var tokens = scope.ServiceProvider.GetRequiredService<IAudioTokenService>();
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());
        var expected = JObject.Parse("""{"frequencyHz":1000,"gainDb":3,"q":1}""");
        if (explicitVersion) expected["scoringVersion"] = 1;
        var oldRound = await tokens.CreateRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId,
            expected.ToString(), [ExploreWebApplicationFactory.Clip, ExploreWebApplicationFactory.Clip],
            filterJson: """{"eqmLevel":"beginner"}""", clipKeys: ["A", "B"]);
        var oldResult = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = oldRound.RoundId, userGuess = "1000|0|1" }));
        oldResult.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        oldResult.GetProperty("detail").GetProperty("scoringVersion").GetInt32().Should().Be(1);
        var oldSnapshot = db.ScoreSnapshots.Single(s => s.ExerciseId == exerciseId);
        ExerciseFilterPresets.Parse(oldSnapshot.FilterJson)["eqmScoringVersion"].Should().Be("1");
        // Model history saved before policy metadata was introduced.
        oldSnapshot.FilterJson = """{"eqmLevel":"beginner"}""";
        await db.SaveChangesAsync();
        var oldSnapshotId = oldSnapshot.Id;
        var oldFilters = oldSnapshot.FilterJson;
        var xpBefore = oldResult.GetProperty("rewards").GetProperty("xp").GetInt32();
        var pilotRound = await tokens.CreateRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId,
            """{"frequencyHz":1000,"gainDb":3,"q":1,"scoringVersion":2}""",
            [ExploreWebApplicationFactory.Clip, ExploreWebApplicationFactory.Clip],
            filterJson: """{"eqmLevel":"beginner","eqmScoringVersion":"fake"}""", clipKeys: ["A", "B"]);
        var result = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = pilotRound.RoundId, userGuess = "1000|0|1" }));

        result.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        result.GetProperty("newCorrectCount").GetInt32().Should().Be(1);
        result.GetProperty("newErrorCount").GetInt32().Should().Be(1);
        result.GetProperty("rewards").GetProperty("xp").GetInt32()
            .Should().Be(xpBefore + Leveling.XpPerWrongAnswer);
        db.ChangeTracker.Clear();
        var history = db.ScoreSnapshots.Single(s => s.Id == oldSnapshotId);
        history.IsCorrect.Should().BeTrue();
        history.FilterJson.Should().Be(oldFilters);
        ExerciseFilterPresets.Parse(history.FilterJson).Should().NotContainKey("eqmScoringVersion");
        var current = db.ScoreSnapshots.Single(s => s.ExerciseId == exerciseId && s.Id != oldSnapshotId);
        current.IsCorrect.Should().BeFalse();
        ExerciseFilterPresets.Parse(current.FilterJson)["eqmScoringVersion"].Should().Be("2");
        db.ScoreAggregates.Single(s => s.ExerciseId == exerciseId).CorrectCount.Should().Be(1);
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
