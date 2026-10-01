using System.Globalization;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.UnitTests;

/// <summary>GamificationService on EF InMemory, with the real resource texts.</summary>
public class GamificationServiceTests
{
    private const string UserId = "player";
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeededExercises_CanEarnEveryBadgeThatDependsOnTheTaxonomy()
    {
        await using var db = NewDatabase();
        SeedData.SeedExercises(db);

        // Ten perfect sessions of 20 answers in every seeded exercise.
        var clock = Start;
        foreach (var exerciseId in await db.Exercises.Select(e => e.ExerciseId).ToListAsync())
        {
            for (var session = 0; session < 10; session++)
            {
                for (var answer = 0; answer < 20; answer++)
                {
                    clock = clock.AddMinutes(1);
                    db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = UserId, ExerciseId = exerciseId, IsCorrect = true, Timestamp = clock });
                }
                clock = clock.AddHours(1);
            }
        }
        await db.SaveChangesAsync();

        var profile = await Service(db, clock).GetProfileAsync(UserId, TimeZoneInfo.Utc);

        var taxonomyBadges = new[]
        {
            BadgeKeys.MasterChords, BadgeKeys.SharpListener, BadgeKeys.RhythmMaestro, BadgeKeys.MelodyExplorer,
            BadgeKeys.ScaleClimber, BadgeKeys.AdvancedConqueror, BadgeKeys.IntervalTamer,
        };
        profile.Badges.Where(b => b.IsEarned).Select(b => b.Key).Should().Contain(taxonomyBadges);
    }

    [Fact]
    public async Task BadgesMissingFromTheTable_AreAwardedOnceSeeded()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = UserId, ExerciseId = 1, IsCorrect = true, Timestamp = Start });
        await db.SaveChangesAsync();
        var service = Service(db, Start.AddMinutes(1));

        var rewards = await service.RecordAttemptAsync(UserId, isCorrect: true, TimeZoneInfo.Utc);

        rewards.NewBadges.Should().BeEmpty("BadgesEarned has a foreign key to Badges");
        rewards.XpGained.Should().Be(Leveling.XpPerCorrectAnswer);
        (await db.BadgesEarned.CountAsync()).Should().Be(0);

        db.Badges.AddRange(SeedData.BadgeSeed());
        await db.SaveChangesAsync();
        var profile = await service.GetProfileAsync(UserId, TimeZoneInfo.Utc);

        profile.Badges.Where(b => b.IsEarned).Select(b => b.Key).Should().Equal(BadgeKeys.FirstSession);
        profile.Progress.Xp.Should().Be(Leveling.XpPerCorrectAnswer + Leveling.XpPerBadge);
    }

    [Fact]
    public async Task Profile_ShowsAvailableBadgesInThePlayersCalendar_AndMarksThemSeen()
    {
        // Flows with this test's async context only.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        await using var db = NewDatabase();
        db.Badges.AddRange(SeedData.BadgeSeed());
        var lateEvening = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc); // Sep 30, 23:00 in Toronto
        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = UserId, ExerciseId = 1, IsCorrect = true, Timestamp = lateEvening });
        db.BadgesEarned.AddRange(
            new BadgesEarned { UserId = UserId, BadgeKey = BadgeKeys.FirstSession, EarnedDate = lateEvening, IsNew = true },
            new BadgesEarned { UserId = UserId, BadgeKey = BadgeKeys.Explorer, EarnedDate = lateEvening, IsNew = false });
        await db.SaveChangesAsync();
        var service = Service(db, lateEvening.AddHours(12));
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

        var profile = await service.GetProfileAsync(UserId, toronto);

        profile.Badges.Select(b => b.Key).Should().Equal(BadgeCatalog.Available.Select(b => b.Key));
        var first = profile.Badges[0];
        first.Should().BeEquivalentTo(new
        {
            Key = BadgeKeys.FirstSession,
            Title = "First notes",
            IsEarned = true,
            EarnedOn = new DateOnly(2026, 9, 30),
            IsNew = true,
        });
        profile.LatestEarned(6).Should().ContainSingle().Which.Key.Should().Be(BadgeKeys.FirstSession);
        profile.Progress.Xp.Should().Be(Leveling.XpPerCorrectAnswer + Leveling.XpPerBadge, "unavailable badges don't count");
        profile.Progress.Streak.Should().Be(new StreakInfo(Current: 1, Best: 1, PracticedToday: false));

        await service.MarkBadgesSeenAsync(UserId);

        (await db.BadgesEarned.AnyAsync(b => b.IsNew)).Should().BeFalse();
    }

    private static ApplicationDbContext NewDatabase() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"gamification-{Guid.NewGuid():N}")
        .Options);

    private static GamificationService Service(ApplicationDbContext db, DateTime nowUtc)
    {
        var localizerFactory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new GamificationService(
            db,
            new PracticeHistory(db),
            new StringLocalizer<SharedResources>(localizerFactory),
            NullLogger<GamificationService>.Instance,
            new FixedClock(nowUtc));
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
