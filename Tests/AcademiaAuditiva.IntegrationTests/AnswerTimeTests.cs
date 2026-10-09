using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The server times each answer, from when <c>RequestPlay</c> issued the round to when
/// <c>ValidateExercise</c> checks it, and counts at most five minutes; a time sent by the
/// browser is ignored. The dashboard adds these times up.
/// </summary>
public class AnswerTimeTests : IClassFixture<AnswerTimeTests.Factory>
{
    private readonly Factory _factory;

    public AnswerTimeTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task EachRound_IsTimedFromItsOwnPlay_NotByTheBrowser()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();
        // The other tests answer GuessNote too: start a practice session of its own.
        _factory.Clock.Advance(PracticeSessions.SessionGap + TimeSpan.FromMinutes(1));

        var roundId = await PlayAsync(client, exerciseId);
        var answer = await ExpectedNoteAsync(exerciseId, roundId);
        _factory.Clock.Advance(TimeSpan.FromSeconds(42));
        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer, timeSpentSeconds = 9999 }));
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        (await SavedSecondsAsync(exerciseId)).Should().Be(42, "the browser's own count is ignored");

        // The next round on the same page starts from its own Play.
        _factory.Clock.Advance(TimeSpan.FromSeconds(30));
        (await AnswerAfterAsync(client, exerciseId, TimeSpan.FromSeconds(10))).Should().Be(10);

        var sessions = await ReadJsonAsync(await client.GetAsync("/Dashboard/GetScoreHistory"));
        var session = sessions.EnumerateArray().First();
        session.GetProperty("correct").GetInt32().Should().Be(2);
        session.GetProperty("timeSpentSeconds").GetInt32()
            .Should().Be(52, "the session adds up its rounds' own seconds, without the pause between them");
    }

    [Fact]
    public async Task ARoundLeftOpen_CountsAtMostFiveMinutes()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();

        (await AnswerAfterAsync(client, exerciseId, TimeSpan.FromMinutes(6))).Should().Be(300);
    }

    [Fact]
    public async Task SheetMusicRounds_AreTimedFromTheirPlayToo()
    {
        var exerciseId = SeedExercise("SolfegeMelody");
        var client = await SignedInClientAsync();

        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));
        var notes = play.GetProperty("melody").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "note")
            .Select(item => item.GetProperty("note").GetString());
        _factory.Clock.Advance(TimeSpan.FromSeconds(25));
        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, userGuess = string.Join("|", notes) }));

        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        (await SavedSecondsAsync(exerciseId)).Should().Be(25);
    }

    [Fact]
    public async Task Dashboard_TotalTime_AddsUpTheRounds()
    {
        var exerciseId = SeedExercise("GuessNote");
        var client = await SignedInClientAsync();
        var before = await TotalMinutesAsync(client);

        await AnswerAfterAsync(client, exerciseId, TimeSpan.FromSeconds(90));
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await AnswerAfterAsync(client, exerciseId, TimeSpan.FromSeconds(90));

        (await TotalMinutesAsync(client)).Should().Be(before + 3, "two rounds of a minute and a half, whatever happened between them");
    }

    /// <summary>Plays a round, answers it right after <paramref name="elapsed"/>, and returns the seconds saved.</summary>
    private async Task<int> AnswerAfterAsync(HttpClient client, int exerciseId, TimeSpan elapsed)
    {
        var roundId = await PlayAsync(client, exerciseId);
        var answer = await ExpectedNoteAsync(exerciseId, roundId);
        _factory.Clock.Advance(elapsed);

        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = answer }));
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        return await SavedSecondsAsync(exerciseId);
    }

    /// <summary>The time saved with the latest answer, which the score, the snapshot and the attempt log agree on.</summary>
    private async Task<int> SavedSecondsAsync(int exerciseId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var score = await db.Scores.Where(s => s.ExerciseId == exerciseId).OrderByDescending(s => s.ScoreId).FirstAsync();
        var snapshot = await db.ScoreSnapshots.Where(s => s.ExerciseId == exerciseId).OrderByDescending(s => s.Id).FirstAsync();

        snapshot.TimeSpentSeconds.Should().Be(score.TimeSpentSeconds);
        _factory.Analytics.Attempts.Last().Attempt.TimeSpentSeconds.Should().Be(score.TimeSpentSeconds);
        return score.TimeSpentSeconds;
    }

    private static async Task<int> TotalMinutesAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Dashboard");
        var total = Regex.Match(html, @">(\d+) <small class=""fs-6 text-secondary"">min</small>");
        total.Success.Should().BeTrue("the dashboard shows the total time");
        return int.Parse(total.Groups[1].Value);
    }

    private static async Task<string> PlayAsync(HttpClient client, int exerciseId) =>
        (await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId })))
            .GetProperty("roundId").GetString()!;

    private int SeedExercise(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = db.ExerciseCategories.SingleOrDefault(c => c.Name == "EarTraining")
            ?? new ExerciseCategory { Name = "EarTraining", DisplayName = "Ear Training" };
        var exercise = new Exercise { Name = name, Description = name, ExerciseCategory = category };
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

    private Task<HttpClient> SignedInClientAsync() =>
        IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) => IntegrationHttp.ReadJsonAsync(response);

    /// <summary>
    /// <see cref="ExploreWebApplicationFactory"/> (no storage) whose time only moves when a
    /// test says so, with an attempt log that records what it is given.
    /// </summary>
    public sealed class Factory : ExploreWebApplicationFactory
    {
        public ManualClock Clock { get; } = new(TimeProvider.System.GetUtcNow());

        public FreePracticeTests.RecordingAnalytics Analytics { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IAnalyticsService>();
                services.AddSingleton<IAnalyticsService>(Analytics);
            });
        }
    }

    public sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by)
        {
            lock (_gate) _now += by;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }
    }
}
