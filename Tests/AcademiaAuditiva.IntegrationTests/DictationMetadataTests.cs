using System.Net.Http.Json;
using AcademiaAuditiva.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A dictation round tells the staff editor what to offer — the time signature, the measures
/// to fill, the note values of the level and its rests — but never the melody or the answer.
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

    private int ExerciseId(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Exercises.Single(e => e.Name == name).ExerciseId;
    }
}
