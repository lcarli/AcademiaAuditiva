using System.Net.Http.Json;
using AcademiaAuditiva.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A dictation round tells the staff editor what to offer — the time signature, the measures
/// to fill, the note values of the level and whether rests are in play — but never the melody
/// or the answer.
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
    [InlineData("MelodicDictation", "md", "1", "w,h", false)]
    [InlineData("MelodicDictation", "md", "4", "w,h,q,8", true)]
    [InlineData("RhythmDictation", "rd", "1", "w,h", false)]
    [InlineData("RhythmDictation", "rd", "4", "w,h,q,8", true)]
    public async Task DictationRound_TellsTheEditorWhatToOffer_ButNotTheAnswer(
        string exercise, string prefix, string level, string durations, bool rests)
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        var filters = new Dictionary<string, string> { [prefix + "Level"] = level, [prefix + "Measures"] = "long" };

        var play = await IntegrationHttp.ReadJsonAsync(
            await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId = ExerciseId(exercise), filters }));

        var metadata = play.GetProperty("metadata");
        metadata.GetProperty("durations").EnumerateArray().Select(d => d.GetString())
            .Should().Equal(durations.Split(','));
        metadata.GetProperty("rests").GetBoolean().Should().Be(rests);
        metadata.GetProperty("numMeasures").GetInt32().Should().Be(4);
        metadata.GetProperty("timeSignature").GetString().Should().BeOneOf("4/4", "3/4", "2/4", "6/8");
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
