using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

public class AudioProgressTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;
    private static readonly DateTime At = new(2026, 1, 10, 14, 0, 0, DateTimeKind.Utc);

    public AudioProgressTests(SignedInWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Exercises.Any()) return;
        SeedData.SeedExercises(db);
        Add(db, "HigherOrLower", "11111111", 30, null);
        Add(db, "LevelMatch", "110000", 10, """{"lmLevel":"beginner","lmDifferenceDb":"6"}""");
        Add(db, "StereoPosition", "11111", 10, """{"spLevel":"beginner","spPosition":"0"}""");
        Add(db, "GuessFrequency", "1000001111", 10, """{"gfLevel":"advanced","gfFrequencyHz":"1000","gfSourceKind":"keys"}""");
        var gfId = db.Exercises.Single(e => e.Name == "GuessFrequency").ExerciseId;
        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = "other-player", ExerciseId = gfId, Timestamp = At, IsCorrect = true });
        db.ScoreAggregates.Add(new ScoreAggregate { UserId = "other-player", ExerciseId = gfId, CorrectCount = 1000, BestScore = 1000 });
        db.Scores.Add(new Score { UserId = SignedInWebApplicationFactory.UserId, ExerciseId = gfId, CorrectCount = 9999 });
        db.SaveChanges();
    }

    [Fact]
    public async Task TrackSelector_ScopesTotalsPathChallengeAndAllCharts_WithoutMixingMusic()
    {
        var music = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync("/Dashboard?culture=en-US"));
        var audio = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync("/Dashboard?track=audio&culture=en-US"));

        Kpi(music, "Total Answers").Should().Be(8);
        Kpi(music, "Best Score").Should().Be(8);
        Kpi(music, "Total Time").Should().Be(4);
        music.Should().Contain("data-track=\"Music\"").And.Contain("GuessTuning");
        Kpi(audio, "Total Answers").Should().Be(21);
        Kpi(audio, "Best Score").Should().Be(5);
        Kpi(audio, "Total Time").Should().Be(3);
        audio.Should().Contain("data-track=\"Audio\"").And.Contain("Audio learning path").And.Contain("Audio daily challenge");
        audio.Should().Contain("id=\"dashboardTrackTabs\"").And.Contain("/Dashboard?track=audio")
            .And.Contain("/LearningPath?track=audio").And.Contain("/Exercise?track=audio");
        audio.Should().Contain("reportUrl(\"GetMostMissedItems\")").And.Contain("reportUrl(\"GetRecommendations\")");
        var challenge = Regex.Match(audio, "<section class=\"aa-card aa-challenge-card.*?</section>", RegexOptions.Singleline).Value;
        foreach (var exercise in new[] { "LevelMatch", "StereoPosition", "GuessFrequency" })
            challenge.Should().Contain($"/Exercise/{exercise}");
        challenge.Should().NotContain("GuessNote");

        using var scope = _factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<UserReportService>();
        (await reports.GetSummaryAsync(SignedInWebApplicationFactory.UserId)).Answers.Should().Be(29,
            "the legacy service call can still explicitly report combined totals");
    }

    [Fact]
    public async Task AudioCharts_ShowOnlyAudioCategories_AndTheDifficultyActuallyPlayed()
    {
        var progress = await GetJsonAsync("GetUserProgress", "audio");
        progress.GetProperty("radar").EnumerateObject().Select(p => p.Name).Should().Equal("AudioComparison");
        progress.GetProperty("byCategory").EnumerateObject().Select(p => (p.Name, p.Value.GetDouble()))
            .Should().Equal(("FrequencyEq", 50.0), ("Level", 33.3), ("StereoPhase", 100.0));
        var difficulty = await GetJsonAsync("GetPerformanceByDifficulty", "Audio");
        difficulty.EnumerateArray().Select(p => (p.GetProperty("difficulty").GetString(), p.GetProperty("accuracy").GetDouble()))
            .Should().Equal(("Beginner", 63.6), ("Advanced", 50.0));
        var music = await GetJsonAsync("GetUserProgress", "music");
        music.GetProperty("byCategory").EnumerateObject().Select(p => (p.Name, p.Value.GetDouble()))
            .Should().Equal(("EarTraining", 100.0));
    }

    [Fact]
    public async Task HistoryTimelineWeakSpotsAndRecommendations_AreScopedBeforeAnalysis()
    {
        var history = await GetJsonAsync("GetScoreHistory", "Audio");
        history.EnumerateArray().Select(p => p.GetProperty("exercise").GetString())
            .Should().BeEquivalentTo("LevelMatch", "StereoPosition", "GuessFrequency");
        var timeline = await GetJsonAsync("GetUserTimeline", "Audio");
        timeline.EnumerateArray().Sum(p => p.GetProperty("answers").GetInt32()).Should().Be(21);
        var struggles = await GetJsonAsync("GetMostMissedItems", "Audio");
        struggles.EnumerateArray().Select(p => p.GetProperty("exercise").GetString())
            .Should().Equal("LevelMatch", "GuessFrequency");
        var recommendations = await GetJsonAsync("GetRecommendations", "Audio");
        recommendations.ToString().Should().Contain("Level Match").And.Contain("Guess Frequency").And.NotContain("Higher or Lower");

        var musicHistory = await GetJsonAsync("GetScoreHistory", "Music");
        musicHistory.EnumerateArray().Select(p => p.GetProperty("exercise").GetString()).Should().Equal("HigherOrLower");
        var musicStruggles = await GetJsonAsync("GetMostMissedItems", "Music");
        musicStruggles.ToString().Should().NotContainAny("LevelMatch", "StereoPosition", "GuessFrequency");
    }

    [Theory]
    [InlineData("en-US", "Audio daily challenge")]
    [InlineData("pt-BR", "Desafio diário de Áudio")]
    [InlineData("fr-CA", "Défi quotidien audio")]
    public async Task AudioDashboard_IsLocalized(string culture, string dailyTitle)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient()
            .GetStringAsync($"/Dashboard?track=Audio&culture={culture}&ui-culture={culture}"));
        html.Should().Contain(dailyTitle).And.NotMatchRegex(@">(?:Dashboard|DailyChallenge|TrainingTrack|LearningPath)\.");
    }

    [Theory]
    [InlineData("/Dashboard?track=bogus")]
    [InlineData("/Dashboard/GetUserProgress?track=bogus")]
    [InlineData("/Dashboard/GetUserTimeline?track=bogus")]
    [InlineData("/Dashboard/GetScoreHistory?track=bogus")]
    [InlineData("/Dashboard/GetPerformanceByDifficulty?track=bogus")]
    [InlineData("/Dashboard/GetMostMissedItems?track=bogus")]
    [InlineData("/Dashboard/GetRecommendations?track=bogus")]
    public async Task UnknownTrack_IsNotSilentlyReportedAsMusicOrAll(string url)
    {
        (await _factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> GetJsonAsync(string action, string track) =>
        await IntegrationHttp.ReadJsonAsync(await _factory.CreateClient()
            .GetAsync($"/Dashboard/{action}?track={track}&culture=en-US&ui-culture=en-US"));

    private static int Kpi(string html, string title) =>
        int.Parse(Regex.Match(html, $@">{title}</h6>\s*<h3[^>]*>(-?\d+)").Groups[1].Value);

    private static void Add(ApplicationDbContext db, string name, string results, int seconds, string? filters)
    {
        var id = db.Exercises.Single(e => e.Name == name).ExerciseId;
        for (var i = 0; i < results.Length; i++)
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId, ExerciseId = id,
                IsCorrect = results[i] == '1', TimeSpentSeconds = seconds,
                Timestamp = At.AddMinutes(i), FilterJson = filters,
            });
        db.ScoreAggregates.Add(new ScoreAggregate
        {
            UserId = SignedInWebApplicationFactory.UserId, ExerciseId = id,
            CorrectCount = results.Count(c => c == '1'), ErrorCount = results.Count(c => c == '0'),
            BestScore = results.Count(c => c == '1'), LastAttemptAt = At,
        });
    }
}
