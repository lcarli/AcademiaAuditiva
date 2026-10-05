using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The student dashboard counts each answer once: its figures come from the answers
/// (ScoreSnapshots) and the totals per exercise (ScoreAggregates), never from the legacy
/// Scores rows, which hold running totals. The player, in Toronto, answered:
/// <list type="bullet">
/// <item>GuessNote 10 times on January 10 (2 wrong) and twice more on January 12;</item>
/// <item>GuessInterval 6 times late on January 11, already January 12 in UTC (4 wrong);</item>
/// <item>GuessQuality 4 times on January 12 (3 wrong), too few to judge the exercise.</item>
/// </list>
/// Every answer took 30 seconds. Another player's answers must not show.
/// </summary>
public class DashboardDataTests : IClassFixture<SignedInWebApplicationFactory>
{
    private const string TimeZoneId = "America/Toronto";
    private const string OtherUserId = "other-student";
    private static readonly DateTime January10 = new(2026, 1, 10, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime January11Evening = new(2026, 1, 12, 3, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime January12 = new(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc);

    private readonly SignedInWebApplicationFactory _factory;

    public DashboardDataTests(SignedInWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeeded();
    }

    [Fact]
    public async Task Totals_CountEachAnswerOnce()
    {
        var html = await GetStringAsync("/Dashboard?culture=en-US");

        Kpi(html, "Total Answers").Should().Be("22");
        Kpi(html, "Best Score").Should().Be("8", "GuessNote's lead over its mistakes peaked at 8");
        Kpi(html, "Total Time").Should().Be("11", "22 answers of 30 seconds");
    }

    [Fact]
    public async Task Timeline_ShowsTheAccuracyOfEachDay_InThePlayersTimeZone()
    {
        var days = await GetJsonAsync("/Dashboard/GetUserTimeline");

        days.EnumerateArray().Select(d => (d.GetProperty("date").GetString(), d.GetProperty("answers").GetInt32(), d.GetProperty("accuracy").GetDouble()))
            .Should().Equal(("2026-01-10", 10, 80.0), ("2026-01-11", 6, 33.3), ("2026-01-12", 6, 50.0));
    }

    [Fact]
    public async Task History_ListsPracticeSessions_LatestFirst()
    {
        var sessions = await GetJsonAsync("/Dashboard/GetScoreHistory");

        sessions.EnumerateArray().Select(s => (
                s.GetProperty("exercise").GetString(),
                s.GetProperty("start").GetDateTime(),
                s.GetProperty("correct").GetInt32(),
                s.GetProperty("errors").GetInt32(),
                s.GetProperty("timeSpentSeconds").GetInt32(),
                s.GetProperty("score").GetInt32()))
            .Should().Equal(
                ("GuessQuality", January12.AddMinutes(10), 1, 3, 120, -2),
                ("GuessNote", January12, 2, 0, 60, 2),
                ("GuessInterval", January11Evening, 2, 4, 180, -2),
                ("GuessNote", January10, 8, 2, 300, 6));
        sessions[0].GetProperty("start").GetString().Should().EndWith("Z", "the page shows the time in the player's own zone");
    }

    [Fact]
    public async Task MostMissed_RanksTheShareOfWrongAnswers_AmongTheLatest()
    {
        var items = await GetJsonAsync("/Dashboard/GetMostMissedItems");

        items.EnumerateArray().Select(i => (i.GetProperty("exercise").GetString(), i.GetProperty("answers").GetInt32(), i.GetProperty("errors").GetInt32(), i.GetProperty("errorRate").GetDouble()))
            .Should().Equal(("GuessInterval", 6, 4, 66.7), ("GuessNote", 12, 2, 16.7));
    }

    [Fact]
    public async Task Recommendations_ReviewTheExercisesAnsweredRightLessThan70Percent()
    {
        var recommendations = await GetJsonAsync("/Dashboard/GetRecommendations?culture=en-US");

        recommendations.EnumerateArray().Select(r => r.GetString())
            .Should().Equal("Review the exercise: Guess Interval");
    }

    [Fact]
    public async Task Charts_ShowTheAccuracyOfEachGroup()
    {
        var progress = await GetJsonAsync("/Dashboard/GetUserProgress");
        var difficulty = await GetJsonAsync("/Dashboard/GetPerformanceByDifficulty");

        Accuracies(progress.GetProperty("radar")).Should().Equal(
            ("ChordRecognition", 25.0), ("IntervalRecognition", 33.3), ("NoteRecognition", 83.3));
        Accuracies(progress.GetProperty("byCategory")).Should().Equal(("EarTraining", 66.7), ("Harmony", 25.0));
        difficulty.EnumerateArray().Select(d => (d.GetProperty("difficulty").GetString(), d.GetProperty("accuracy").GetDouble()))
            .Should().Equal(("Beginner", 66.7), ("Intermediate", 25.0));
    }

    private static IEnumerable<(string, double)> Accuracies(JsonElement groups) =>
        groups.EnumerateObject().Select(g => (g.Name, g.Value.GetDouble()));

    private static string Kpi(string html, string label)
    {
        var kpi = Regex.Match(html, $@">{label}</h6>\s*<h3[^>]*>(-?\d+)");
        kpi.Success.Should().BeTrue("the dashboard shows {0}", label);
        return kpi.Groups[1].Value;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await SendAsync(url);
        return await IntegrationHttp.ReadJsonAsync(response);
    }

    private async Task<string> GetStringAsync(string url)
    {
        using var response = await SendAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    // Requests carry the player's time zone, as the browser's cookie does.
    private Task<HttpResponseMessage> SendAsync(string url)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{UserTimeZone.CookieName}={Uri.EscapeDataString(TimeZoneId)}");
        return client.SendAsync(request);
    }

    private void EnsureSeeded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Exercises.Any()) return;

        SeedData.SeedExercises(db);
        db.Users.Add(new IdentityUser { Id = OtherUserId, UserName = "other@example.test", Email = "other@example.test" });
        var ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);

        var player = SignedInWebApplicationFactory.UserId;
        Answer(db, player, ids["GuessNote"], January10, "1101110111");
        Answer(db, player, ids["GuessInterval"], January11Evening, "110000");
        Answer(db, player, ids["GuessNote"], January12, "11");
        Answer(db, player, ids["GuessQuality"], January12.AddMinutes(10), "0010");
        Totals(db, player, ids["GuessNote"], correct: 10, errors: 2, best: 8);
        Totals(db, player, ids["GuessInterval"], correct: 2, errors: 4, best: 2);
        Totals(db, player, ids["GuessQuality"], correct: 1, errors: 3, best: 0);

        // Legacy rows the dashboard used to add up, and later than every answer.
        foreach (var exercise in new[] { "GuessNote", "GuessInterval", "GuessQuality" })
        {
            db.Scores.Add(new Score
            {
                UserId = player,
                ExerciseId = ids[exercise],
                CorrectCount = 5000,
                ErrorCount = 4000,
                BestScore = 999,
                TimeSpentSeconds = 99_999,
                Timestamp = January12.AddDays(10),
            });
        }

        // Another player, wrong every time, later still.
        Answer(db, OtherUserId, ids["GuessChords"], January12.AddDays(20), new string('0', 30));
        Totals(db, OtherUserId, ids["GuessChords"], correct: 0, errors: 30, best: 50);

        db.SaveChanges();
    }

    /// <summary>Answers a minute apart from <paramref name="start"/>: 1 for right, 0 for wrong.</summary>
    private static void Answer(ApplicationDbContext db, string userId, int exerciseId, DateTime start, string results)
    {
        for (var i = 0; i < results.Length; i++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = userId,
                ExerciseId = exerciseId,
                IsCorrect = results[i] == '1',
                TimeSpentSeconds = 30,
                Timestamp = start.AddMinutes(i),
            });
        }
    }

    private static void Totals(ApplicationDbContext db, string userId, int exerciseId, int correct, int errors, int best) =>
        db.ScoreAggregates.Add(new ScoreAggregate
        {
            UserId = userId,
            ExerciseId = exerciseId,
            CorrectCount = correct,
            ErrorCount = errors,
            BestScore = best,
            LastAttemptAt = DateTime.UtcNow,
        });
}
