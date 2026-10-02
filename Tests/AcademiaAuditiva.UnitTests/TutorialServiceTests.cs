using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademiaAuditiva.UnitTests;

/// <summary>TutorialService on EF InMemory; TutorialSqlTests covers parallel saves.</summary>
public class TutorialServiceTests
{
    private const string UserId = "player";
    private static readonly DateTime FirstClose = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkSeen_KeepsOneRowPerTour_WithTheFirstCloseTime()
    {
        await using var db = NewDatabase();

        await Service(db, FirstClose).MarkSeenAsync(UserId, TutorialCatalog.Dashboard, finished: false);
        await Service(db, FirstClose.AddDays(1)).MarkSeenAsync(UserId, TutorialCatalog.Dashboard, finished: false);

        var row = await db.UserTutorials.AsNoTracking().SingleAsync();
        row.Should().BeEquivalentTo(new { UserId, TutorialKey = TutorialCatalog.Dashboard, SeenAt = FirstClose, Finished = false });
    }

    [Fact]
    public async Task MarkSeen_RecordsAFinishedReplay_AndASkipNeverUndoesIt()
    {
        await using var db = NewDatabase();
        await Service(db, FirstClose).MarkSeenAsync(UserId, TutorialCatalog.Exercise, finished: false);

        await Service(db, FirstClose.AddHours(1)).MarkSeenAsync(UserId, TutorialCatalog.Exercise, finished: true);
        (await db.UserTutorials.AsNoTracking().SingleAsync()).Finished.Should().BeTrue("the replay reached the last step");

        await Service(db, FirstClose.AddHours(2)).MarkSeenAsync(UserId, TutorialCatalog.Exercise, finished: false);
        var row = await db.UserTutorials.AsNoTracking().SingleAsync();
        row.Finished.Should().BeTrue();
        row.SeenAt.Should().Be(FirstClose);
    }

    [Fact]
    public async Task HasSeen_IsPerUserAndTour()
    {
        await using var db = NewDatabase();
        var service = Service(db, FirstClose);

        await service.MarkSeenAsync(UserId, TutorialCatalog.Dashboard, finished: true);

        (await service.HasSeenAsync(UserId, TutorialCatalog.Dashboard)).Should().BeTrue();
        (await service.HasSeenAsync(UserId, TutorialCatalog.Exercise)).Should().BeFalse();
        (await service.HasSeenAsync("someone-else", TutorialCatalog.Dashboard)).Should().BeFalse();
    }

    private static ApplicationDbContext NewDatabase() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"tutorials-{Guid.NewGuid():N}")
        .Options);

    private static TutorialService Service(ApplicationDbContext db, DateTime nowUtc)
        => new(db, new FixedClock(nowUtc), NullLogger<TutorialService>.Instance);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
