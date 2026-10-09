using System.Globalization;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>The student dashboard's queries on SQL Server, which EF InMemory doesn't translate.</summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class DashboardSqlTests
{
    private static readonly DateTime At = new(2026, 1, 10, 14, 0, 0, DateTimeKind.Utc);
    private readonly RealSqlServerFixture _fixture;

    public DashboardSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task Dashboard_ReadsTheAnswersAndTheTotals()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await CreatePlayerAsync(db);
        var ids = await db.Exercises.ToDictionaryAsync(e => e.Name, e => e.ExerciseId);
        AddAnswers(db, userId, ids["GuessNote"], At, "110111", seconds: 20);
        AddAnswers(db, userId, ids["GuessInterval"], At.AddHours(2), "10000", seconds: 10);
        db.ScoreAggregates.AddRange(
            new ScoreAggregate { UserId = userId, ExerciseId = ids["GuessNote"], CorrectCount = 5, ErrorCount = 1, BestScore = 4, LastAttemptAt = At },
            new ScoreAggregate { UserId = userId, ExerciseId = ids["GuessInterval"], CorrectCount = 1, ErrorCount = 4, BestScore = 1, LastAttemptAt = At });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<UserReportService>();

        (await service.GetSummaryAsync(userId)).Should().Be(new DashboardSummary(11, 4, 170));

        var skills = await service.GetSkillProfileAsync(userId);
        skills.Radar.Select(g => (g.Key, g.Value)).Should().Equal(("IntervalRecognition", 20.0), ("NoteRecognition", 83.3));
        skills.ByCategory.Select(g => (g.Key, g.Value)).Should().Equal(("EarTraining", 54.5));
        (await service.GetAccuracyByDifficultyAsync(userId)).Should().Equal(new DifficultyAccuracy("Beginner", 54.5));

        (await service.GetTimelineAsync(userId, TimeZoneInfo.Utc)).Select(d => (d.Date, d.Answers, d.Accuracy))
            .Should().Equal(("2026-01-10", 11, 54.5));
        (await service.GetRecentSessionsAsync(userId)).Select(s => (s.Exercise, s.Start, s.Correct, s.Errors, s.TimeSpentSeconds))
            .Should().Equal(("GuessInterval", At.AddHours(2), 1, 4, 50), ("GuessNote", At, 5, 1, 120));
        (await service.GetStrugglesAsync(userId)).Select(f => (f.Exercise, f.Answers, f.Errors))
            .Should().Equal(("GuessInterval", 5, 4), ("GuessNote", 6, 1));
        (await service.GetRecommendationsAsync(userId)).Should().Equal("Review the exercise: Guess Interval");
    }

    [RealSqlFact]
    public async Task TrackQueries_TranslateToSql_AndKeepAudioAnswersOutOfMusic()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await CreatePlayerAsync(db);
        var ids = await db.Exercises.ToDictionaryAsync(e => e.Name, e => e.ExerciseId);
        AddAnswers(db, userId, ids["HigherOrLower"], At, "11111111", 30);
        AddAnswers(db, userId, ids["GuessFrequency"], At.AddHours(1), "10000", 10);
        foreach (var answer in db.ScoreSnapshots.Local.Where(s => s.ExerciseId == ids["GuessFrequency"]))
            answer.FilterJson = """{"gfLevel":"advanced","gfFrequencyHz":"1000"}""";
        db.ScoreAggregates.AddRange(
            new ScoreAggregate { UserId = userId, ExerciseId = ids["HigherOrLower"], CorrectCount = 8, BestScore = 8, LastAttemptAt = At },
            new ScoreAggregate { UserId = userId, ExerciseId = ids["GuessFrequency"], CorrectCount = 1, ErrorCount = 4, BestScore = 1, LastAttemptAt = At });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<UserReportService>();

        (await service.GetSummaryAsync(userId, track: TrainingTracks.Music)).Should().Be(new DashboardSummary(8, 8, 240));
        (await service.GetSummaryAsync(userId, track: TrainingTracks.Audio)).Should().Be(new DashboardSummary(5, 1, 50));
        (await service.GetSkillProfileAsync(userId, track: TrainingTracks.Audio)).ByCategory
            .Should().BeEquivalentTo(new Dictionary<string, double> { ["FrequencyEq"] = 20 });
        (await service.GetAccuracyByDifficultyAsync(userId, track: TrainingTracks.Audio))
            .Should().Equal(new DifficultyAccuracy("Advanced", 20));
        (await service.GetRecentSessionsAsync(userId, track: TrainingTracks.Audio)).Select(s => s.Exercise)
            .Should().Equal("GuessFrequency");
        (await service.GetTimelineAsync(userId, TimeZoneInfo.Utc, track: TrainingTracks.Music)).Sum(s => s.Answers).Should().Be(8);
        (await service.GetStrugglesAsync(userId, track: TrainingTracks.Audio)).Select(s => s.Exercise).Should().Equal("GuessFrequency");
        (await service.GetRecommendationsAsync(userId, track: TrainingTracks.Audio)).Should().Equal("Review the exercise: Guess Frequency");
    }

    [RealSqlFact]
    public async Task Dashboard_OfANewPlayer_IsEmpty()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        using var scope = _fixture.Services.CreateScope();
        var userId = await CreatePlayerAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var service = scope.ServiceProvider.GetRequiredService<UserReportService>();

        (await service.GetSummaryAsync(userId)).Should().Be(new DashboardSummary(0, 0, 0));
        (await service.GetSkillProfileAsync(userId)).Radar.Should().BeEmpty();
        (await service.GetTimelineAsync(userId, TimeZoneInfo.Utc)).Should().BeEmpty();
        (await service.GetRecentSessionsAsync(userId)).Should().BeEmpty();
        (await service.GetRecommendationsAsync(userId)).Should().Equal("Keep practicing more exercises to improve your performance.");
    }

    private static async Task<string> CreatePlayerAsync(ApplicationDbContext db)
    {
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
        var email = $"dashboard.{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Answers a minute apart from <paramref name="start"/>: 1 for right, 0 for wrong.</summary>
    private static void AddAnswers(ApplicationDbContext db, string userId, int exerciseId, DateTime start, string results, int seconds)
    {
        for (var i = 0; i < results.Length; i++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = userId,
                ExerciseId = exerciseId,
                IsCorrect = results[i] == '1',
                TimeSpentSeconds = seconds,
                Timestamp = start.AddMinutes(i),
            });
        }
    }
}
