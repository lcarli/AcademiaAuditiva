using System.Net.Http.Json;
using AcademiaAuditiva.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A dictation round tells the staff editor what to offer — the time signature, the measures
/// to fill, the note values of the level and its rests — but never the melody or the answer.
/// A GuessRhythmPattern round tells the page the rhythms to draw, but not which one is played.
/// </summary>
public class DictationMetadataTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public DictationMetadataTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("MelodicDictation", "md", "1", "w,h", "")]
    [InlineData("MelodicDictation", "md", "4", "w,h,q,8", "wr,hr,qr,8r")]
    [InlineData("RhythmDictation", "rd", "1", "w,h", "")]
    [InlineData("RhythmDictation", "rd", "4", "w,h,q,8", "wr,hr,qr,8r")]
    [InlineData("RhythmDictation", "rd", "5", "h.,h,q.,q,8", "qr")]
    [InlineData("RhythmDictation", "rd", "6", "h,q,8.,8,16", "qr")]
    [InlineData("RhythmDictation", "rd", "7", "h,q,8", "qr,8r")]
    [InlineData("RhythmDictation", "rd", "8", "h.,q.,q,8", "q.r")]
    public async Task DictationRound_TellsTheEditorWhatToOffer_ButNotTheAnswer(
        string exercise, string prefix, string level, string durations, string restDurations)
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        var filters = new Dictionary<string, string>
        {
            [prefix + "Level"] = level, [prefix + "Measures"] = "long", [prefix + "Tempo"] = "60",
        };

        var play = await IntegrationHttp.ReadJsonAsync(
            await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId = ExerciseId(exercise), filters }));

        // A melody in 6/8 is written in the figures of compound meter.
        var metadata = play.GetProperty("metadata");
        var timeSignature = metadata.GetProperty("timeSignature").GetString();
        var compound = exercise == "MelodicDictation" && timeSignature == "6/8";
        metadata.GetProperty("durations").EnumerateArray().Select(d => d.GetString())
            .Should().Equal(compound ? new[] { "h.", "q.", "q", "8" } : durations.Split(','));
        metadata.GetProperty("restDurations").EnumerateArray().Select(d => d.GetString())
            .Should().Equal(compound ? new[] { "q.r" } : restDurations.Split(',', StringSplitOptions.RemoveEmptyEntries));
        metadata.GetProperty("rests").GetBoolean().Should().Be(compound || restDurations.Length > 0);
        metadata.GetProperty("numMeasures").GetInt32().Should().Be(4);
        timeSignature.Should().BeOneOf(level == "8" ? new[] { "6/8" } : new[] { "4/4", "3/4", "2/4", "6/8" });
        metadata.TryGetProperty("firstNote", out _).Should().Be(exercise == "MelodicDictation",
            "only the melody gives its first note");
        metadata.TryGetProperty("melody", out _).Should().BeFalse();
        metadata.TryGetProperty("answerString", out _).Should().BeFalse();
        play.TryGetProperty("answerString", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("1", "4/4")]
    [InlineData("5", "4/4,3/4,2/4")]
    [InlineData("8", "6/8")]
    public async Task RhythmPatternRound_OffersFourRhythms_ButNotWhichIsPlayed(string level, string timeSignatures)
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        var filters = new Dictionary<string, string> { ["grpLevel"] = level, ["grpTempo"] = "60" };

        var play = await IntegrationHttp.ReadJsonAsync(
            await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId = ExerciseId("GuessRhythmPattern"), filters }));

        var metadata = play.GetProperty("metadata");
        metadata.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["timeSignature", "numMeasures", "level", "options"], "the page needs the rhythms to draw, and nothing else");
        metadata.GetProperty("timeSignature").GetString().Should().BeOneOf(timeSignatures.Split(','));
        metadata.GetProperty("numMeasures").GetInt32().Should().Be(2);
        metadata.GetProperty("level").GetInt32().Should().Be(int.Parse(level));
        metadata.GetProperty("options").EnumerateArray().Select(o => o.GetString())
            .Should().HaveCount(4).And.OnlyHaveUniqueItems().And.OnlyContain(o => o!.Contains("|bar|"));
        play.TryGetProperty("answerString", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RhythmPatternRound_IsRightForTheRhythmPlayed_AndWrongForTheOthers(bool pickThePlayedOne)
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        var exerciseId = ExerciseId("GuessRhythmPattern");
        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay",
            new { exerciseId, free = true, filters = new Dictionary<string, string> { ["grpLevel"] = "7" } }));
        var roundId = play.GetProperty("roundId").GetString();
        var options = play.GetProperty("metadata").GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToList();

        var played = (await IntegrationHttp.ReadJsonAsync(
            await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId }))).GetProperty("answer").GetString();
        var guess = pickThePlayedOne ? played : options.First(option => option != played);
        var validation = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = guess }));

        options.Should().Contain(played!, "the rhythm played is one of those offered");
        validation.GetProperty("isCorrect").GetBoolean().Should().Be(pickThePlayedOne);
        validation.GetProperty("answer").GetString().Should().Be(played, "the rhythm played is shown as it was offered");
    }

    private int ExerciseId(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Exercises.Single(e => e.Name == name).ExerciseId;
    }
}
