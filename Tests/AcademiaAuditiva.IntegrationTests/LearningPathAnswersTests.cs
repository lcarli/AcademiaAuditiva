using System.Net.Http.Json;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// ValidateExercise reports learning path progress when the answer is on the player's
/// current step, and a celebration when it completes the step, its unit or the path.
/// The expected answer is cached the way RequestPlay caches it for sheet-music exercises.
/// </summary>
public class LearningPathAnswersTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;

    public LearningPathAnswersTests(SignedInWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AnswersOnTheCurrentStep_ShowProgress_UntilOneCompletesIt()
    {
        ResetAnswers(("HigherOrLower", 7));
        var client = await CreateClientAsync();

        var path = await AnswerAsync(client, "HigherOrLower", correct: false);

        path.GetProperty("label").GetString().Should().Be("Learning path");
        path.GetProperty("text").GetString().Should().Be("7 of 8 right answers");
        path.GetProperty("percent").GetInt32().Should().Be(87);
        path.GetProperty("completed").GetBoolean().Should().BeFalse();
        path.GetProperty("celebration").ValueKind.Should().Be(JsonValueKind.Null);

        path = await AnswerAsync(client, "HigherOrLower", correct: true);

        path.GetProperty("text").GetString().Should().Be("Step complete!");
        path.GetProperty("percent").GetInt32().Should().Be(100);
        path.GetProperty("completed").GetBoolean().Should().BeTrue();
        ShouldCelebrate(path,
            title: "Step complete!",
            icon: "bi-check2-circle",
            text: "You completed step 1: Higher or Lower.",
            nextText: "Next step: Guess Interval",
            actionText: "Go to the next step",
            actionUrl: "/Exercise/GuessInterval?keySelect=C4&scaleTypeSelect=major");

        (await AnswerAsync(client, "HigherOrLower", correct: true)).ValueKind
            .Should().Be(JsonValueKind.Null, "the step is already complete");
        (await AnswerAsync(client, "GuessMissingNote", correct: true)).ValueKind
            .Should().Be(JsonValueKind.Null, "the step is locked");
    }

    [Fact]
    public async Task TheLastStepOfAUnit_CelebratesTheUnit()
    {
        ResetAnswers(("HigherOrLower", 8), ("GuessInterval", 7), ("GuessChords", 8), ("GuessMissingNote", 7), ("GuessDegree", 7), ("GuessMeter", 6));
        var client = await CreateClientAsync();

        var path = await AnswerAsync(client, "GuessMeter", correct: true);

        ShouldCelebrate(path,
            title: "Unit complete!",
            icon: "bi-flag",
            text: "You completed step 6: Guess Meter.",
            nextText: "Next step: Melodic Intervals",
            actionText: "Go to the next step",
            actionUrl: "/Exercise/IntervalMelodico?keySelect=C&scaleTypeSelect=major");
    }

    [Fact]
    public async Task TheLastStep_CelebratesThePath()
    {
        var steps = LearningPathCatalog.Steps;
        ResetAnswers(steps.Take(steps.Count - 1).Select(s => (s.Exercise, s.Required)).Append(("GuessNote", 4)).ToArray());
        var client = await CreateClientAsync();

        var path = await AnswerAsync(client, "GuessNote", correct: true);

        ShouldCelebrate(path,
            title: "Path complete!",
            icon: "bi-trophy",
            text: "You completed step 24: Guess Note.",
            nextText: null,
            actionText: "View the path",
            actionUrl: "/LearningPath");
    }

    private static void ShouldCelebrate(
        JsonElement path, string title, string icon, string text, string? nextText, string actionText, string actionUrl)
    {
        path.GetProperty("completed").GetBoolean().Should().BeTrue();
        var celebration = path.GetProperty("celebration");
        celebration.GetProperty("title").GetString().Should().Be(title);
        celebration.GetProperty("icon").GetString().Should().Be(icon);
        celebration.GetProperty("text").GetString().Should().Be(text);
        celebration.GetProperty("nextText").GetString().Should().Be(nextText);
        celebration.GetProperty("actionText").GetString().Should().Be(actionText);
        celebration.GetProperty("actionUrl").GetString().Should().Be(actionUrl);
        celebration.GetProperty("closeText").GetString().Should().Be("Keep practicing");
    }

    /// <summary>Replaces the player's history with right answers given an hour ago, in order.</summary>
    private void ResetAnswers(params (string Exercise, int Count)[] rightAnswers)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
        db.ScoreSnapshots.RemoveRange(db.ScoreSnapshots.Where(s => s.UserId == SignedInWebApplicationFactory.UserId));

        var ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);
        var at = DateTime.UtcNow.AddHours(-1);
        foreach (var (exercise, count) in rightAnswers)
        {
            for (var i = 0; i < count; i++)
            {
                at = at.AddSeconds(5);
                db.ScoreSnapshots.Add(new ScoreSnapshot
                {
                    UserId = SignedInWebApplicationFactory.UserId,
                    ExerciseId = ids[exercise],
                    IsCorrect = true,
                    TimeSpentSeconds = 5,
                    Timestamp = at,
                });
            }
        }
        db.SaveChanges();
    }

    private Task<HttpClient> CreateClientAsync() => IntegrationHttp.WithAntiforgeryHeaderAsync(
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    /// <summary>Caches a round's expected answer, answers it and returns the "path" feedback.</summary>
    private async Task<JsonElement> AnswerAsync(HttpClient client, string exercise, bool correct)
    {
        int exerciseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            exerciseId = db.Exercises.Single(e => e.Name == exercise).ExerciseId;
        }
        var (expected, right, wrong) = exercise == "GuessNote"
            ? ("""{"note":"C4"}""", "C4", "D4")
            : ("""{"answer":"higher"}""", "higher", "lower");
        await _factory.Services.GetRequiredService<IDistributedCache>().SetStringAsync(
            $"ExerciseAnswer:{SignedInWebApplicationFactory.UserId}:{exerciseId}",
            JsonSerializer.Serialize(new { ExpectedAnswer = expected }));

        var validation = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync(
            "/Exercise/ValidateExercise", new { exerciseId, userGuess = correct ? right : wrong }));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().Be(correct);
        return validation.GetProperty("path");
    }
}
