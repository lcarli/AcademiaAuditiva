using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>DailyChallengeService on EF InMemory with the seeded exercises.</summary>
public class DailyChallengeServiceTests
{
    private const string UserId = "player";

    [Fact]
    public async Task Today_IsThePlayersLocalDate_AndCountsOnlyTheirAnswersOfThatDay()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"daily-challenge-service-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        var now = new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc); // Oct 5, 22:00 in Toronto
        var today = new DateOnly(2026, 10, 5);
        var exercises = await db.Exercises
            .Select(e => new ChallengeExercise(e.ExerciseId, e.Name, e.ExerciseCategory.Name))
            .ToListAsync();
        var first = DailyChallengeRules.Pick(today, exercises)[0];
        db.ScoreSnapshots.AddRange(
            new ScoreSnapshot { UserId = UserId, ExerciseId = first.ExerciseId, IsCorrect = false, Timestamp = now.AddHours(-1) },
            new ScoreSnapshot { UserId = UserId, ExerciseId = first.ExerciseId, IsCorrect = true, Timestamp = now.AddHours(-23) }, // Oct 4 in Toronto
            new ScoreSnapshot { UserId = "someone-else", ExerciseId = first.ExerciseId, IsCorrect = true, Timestamp = now.AddHours(-1) });
        await db.SaveChangesAsync();
        var service = new DailyChallengeService(db, new PracticeHistory(db), new FixedClock(now));

        var progress = await service.GetTodayAsync(UserId, toronto);

        progress.Date.Should().Be(today);
        progress.Items.Select(i => i.Exercise).Should().Equal(DailyChallengeRules.Pick(today, exercises).Select(e => e.Name));
        progress.Items[0].Should().Be(new ChallengeItem(first.Name, first.Category, 1, DailyChallengeRules.Target(first.Name)));
        progress.Items.Skip(1).Should().OnlyContain(i => i.Answered == 0);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    [Fact]
    public async Task AudioToday_UsesTheSameLocalCalendar_ButOnlyAudioAnswersOfThisPlayer()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audio-daily-challenge-{Guid.NewGuid():N}").Options);
        SeedData.SeedExercises(db);
        var now = new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc);
        var ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);
        db.ScoreSnapshots.AddRange(
            new ScoreSnapshot { UserId = UserId, ExerciseId = ids["GuessFrequency"], IsCorrect = false, Timestamp = now.AddHours(-1) },
            new ScoreSnapshot { UserId = UserId, ExerciseId = ids["GuessFrequency"], IsCorrect = true, Timestamp = now.AddHours(-23) },
            new ScoreSnapshot { UserId = "other", ExerciseId = ids["GuessFrequency"], IsCorrect = true, Timestamp = now.AddHours(-1) },
            new ScoreSnapshot { UserId = UserId, ExerciseId = ids["GuessNote"], IsCorrect = true, Timestamp = now });
        await db.SaveChangesAsync();
        var service = new DailyChallengeService(db, new PracticeHistory(db), new FixedClock(now));

        var progress = await service.GetTodayAsync(UserId, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto"), TrainingTracks.Audio);

        progress.Date.Should().Be(new DateOnly(2026, 10, 5));
        progress.Track.Should().Be(TrainingTracks.Audio);
        progress.Items.Select(i => i.Exercise).Should().BeEquivalentTo("LevelMatch", "StereoPosition", "GuessFrequency");
        progress.Items.Single(i => i.Exercise == "GuessFrequency").Answered.Should().Be(1);
        progress.Items.Where(i => i.Exercise != "GuessFrequency").Should().OnlyContain(i => i.Answered == 0);
    }
}
