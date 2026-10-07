using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Free practice: a round played with <c>free: true</c> is checked like any other, but
/// nothing is saved (score, attempt log, XP, badges) and its answer can be shown before
/// it is answered. The mode is fixed when the round is played, never by the answer.
/// </summary>
public class FreePracticeTests : IClassFixture<FreePracticeTests.Factory>
{
    private const string SessionExpired = "Session expired or no answer found.";

    private readonly Factory _factory;

    public FreePracticeTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task FreeRound_ShowsItsAnswer_AndIsCheckedWithoutSavingAnything()
    {
        var exerciseId = SeedExercise("GuessNote");
        var otherExerciseId = SeedExercise("GuessInterval");
        var client = await SignedInClientAsync();
        var before = await SavedAsync();

        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true }));
        play.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "playToken");
        var roundId = play.GetProperty("roundId").GetString();

        var reveal = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId }));
        reveal.EnumerateObject().Select(p => p.Name).Should().Equal("success", "answer");
        reveal.GetProperty("success").GetBoolean().Should().BeTrue();
        var answer = reveal.GetProperty("answer").GetString();
        answer.Should().Be(await ExpectedNoteAsync(exerciseId, roundId!));

        // Showing the answer does not use the round up, and only works for its own exercise.
        (await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId })))
            .GetProperty("answer").GetString().Should().Be(answer);
        var elsewhere = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId = otherExerciseId, roundId }));
        elsewhere.GetProperty("success").GetBoolean().Should().BeFalse();

        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        validation.EnumerateObject().Select(p => p.Name).Should().Equal("success", "free", "isCorrect", "answer", "detail", "message");
        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("free").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        validation.GetProperty("answer").GetString().Should().Be(answer);
        validation.GetProperty("detail").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "only RhythmTap says how the answer went");
        validation.GetProperty("message").GetString().Should().Be("Correct answer!");

        // An answered round is used up, free or not.
        var replay = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        replay.GetProperty("success").GetBoolean().Should().BeFalse();
        var late = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId }));
        late.GetProperty("success").GetBoolean().Should().BeFalse();
        late.GetProperty("message").GetString().Should().Be(SessionExpired);

        (await SavedAsync()).Should().Be(before);
    }

    [Fact]
    public async Task FreeRound_ChecksAWrongAnswer_WithoutSavingIt()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();
        var before = await SavedAsync();

        var roundId = (await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true })))
            .GetProperty("roundId").GetString();
        var expected = await ExpectedNoteAsync(exerciseId, roundId!);
        var wrong = expected.StartsWith('C') ? "D" : "C";

        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = wrong }));

        validation.GetProperty("free").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        validation.GetProperty("answer").GetString().Should().Be(expected);
        validation.GetProperty("message").GetString().Should().Be("Incorrect answer.");
        (await SavedAsync()).Should().Be(before);
    }

    [Fact]
    public async Task ScoredRound_NeverShowsItsAnswer_AndTheAnswerCannotMakeItFree()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();
        var before = await SavedAsync();

        var roundId = (await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId })))
            .GetProperty("roundId").GetString();

        var reveal = await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId, roundId });
        reveal.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var answer = await ExpectedNoteAsync(exerciseId, roundId!);
        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer, free = true }));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.TryGetProperty("free", out _).Should().BeFalse();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        validation.GetProperty("newCorrectCount").GetInt32().Should().Be(1);
        validation.GetProperty("rewards").ValueKind.Should().Be(JsonValueKind.Object);
        (await SavedAsync()).Should().BeEquivalentTo(before with
        {
            Scores = before.Scores + 1,
            Snapshots = before.Snapshots + 1,
            Aggregates = before.Aggregates + 1,
            Attempts = before.Attempts + 1,
        }, options => options.Excluding(saved => saved.Badges));
    }

    [Fact]
    public async Task FreeSolfegeRound_IsCheckedWithoutSavingAnything()
    {
        var exerciseId = SeedExercise("SolfegeMelody");
        var client = await SignedInClientAsync();
        var before = await SavedAsync();

        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true }));
        var notes = play.GetProperty("melody").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "note")
            .Select(item => item.GetProperty("note").GetString());

        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, userGuess = string.Join("|", notes) }));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("free").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        (await SavedAsync()).Should().Be(before);

        var replay = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, userGuess = string.Join("|", notes) }));
        replay.GetProperty("success").GetBoolean().Should().BeFalse("the expected answer is used only once");
    }

    [Fact]
    public async Task RevealAnswer_ForAnUnknownRound_SaysTheSessionExpired()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();

        var json = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer",
            new { exerciseId, roundId = "0123456789abcdef0123456789abcdef" }));

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("message").GetString().Should().Be(SessionExpired);
    }

    [Fact]
    public async Task RevealAnswer_ForAnUnknownExercise_IsNotFound()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId = 987654, roundId = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{"exerciseId":1}""")]
    [InlineData("""{"exerciseId":1,"roundId":""}""")]
    [InlineData("""{"exerciseId":"one","roundId":"abc"}""")]
    public async Task RevealAnswer_RefusesRequestsWithoutARound(string body)
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/Exercise/RevealAnswer", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevealAnswer_NeedsTheAntiforgeryToken()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/Exercise/RevealAnswer", new { exerciseId = 1, roundId = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private int SeedExercise(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exercise = new Exercise { Name = name, Description = name };
        db.Exercises.Add(exercise);
        db.SaveChanges();
        return exercise.ExerciseId;
    }

    private async Task<string> ExpectedNoteAsync(int exerciseId, string roundId)
    {
        var round = await _factory.Services.GetRequiredService<IAudioTokenService>()
            .GetRoundAsync(SignedInWebApplicationFactory.UserId, exerciseId, roundId);
        round.Should().NotBeNull();
        return (string)JObject.Parse(round!.ExpectedAnswerJson)["note"]!;
    }

    private sealed record Saved(int Scores, int Snapshots, int Aggregates, int Badges, int Attempts);

    private async Task<Saved> SavedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new Saved(
            await db.Scores.CountAsync(),
            await db.ScoreSnapshots.CountAsync(),
            await db.ScoreAggregates.CountAsync(),
            await db.BadgesEarned.CountAsync(),
            _factory.Analytics.Attempts.Count);
    }

    private Task<HttpClient> SignedInClientAsync() =>
        IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) => IntegrationHttp.ReadJsonAsync(response);

    /// <summary>
    /// <see cref="ExploreWebApplicationFactory"/> (no storage) with the seeded badges, so a
    /// saved answer would earn one, and an attempt log that records what it is given.
    /// </summary>
    public sealed class Factory : ExploreWebApplicationFactory
    {
        public RecordingAnalytics Analytics { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAnalyticsService>();
                services.AddSingleton<IAnalyticsService>(Analytics);

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (!db.Badges.Any())
                {
                    db.Badges.AddRange(SeedData.BadgeSeed());
                    db.SaveChanges();
                }
            });
        }
    }

    public sealed class RecordingAnalytics : IAnalyticsService
    {
        public ConcurrentQueue<ExerciseAttemptLog> Attempts { get; } = new();

        public Task SaveAttemptAsync(ExerciseAttemptLog log)
        {
            Attempts.Enqueue(log);
            return Task.CompletedTask;
        }

        public Task<List<ExerciseAttemptLog>> GetAttemptsAsync(string userId, string? exercise = null)
            => Task.FromResult(Attempts.Where(a => a.UserId == userId && (exercise is null || a.Exercise == exercise)).ToList());

        public Task DeleteAttemptsAsync(string userId) => Task.CompletedTask;
    }
}
