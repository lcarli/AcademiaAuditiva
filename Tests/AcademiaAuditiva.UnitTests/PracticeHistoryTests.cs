using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>PracticeHistory reads the answers once per context and again after an answer is saved.</summary>
public class PracticeHistoryTests
{
    private const string UserId = "player";
    // Unspecified, as SQL Server returns datetime2 values.
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0);

    [Fact]
    public async Task Answers_AreThePlayersOnly_OldestFirst_InUtc()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.AddRange(
            Answer(exerciseId: 1, Start.AddMinutes(2)),
            Answer(exerciseId: 2, Start),
            Answer(exerciseId: 3, Start.AddMinutes(2)),
            Answer(exerciseId: 4, Start.AddMinutes(1), userId: "someone-else"));
        await db.SaveChangesAsync();

        var answers = await new PracticeHistory(db).GetAsync(UserId);

        answers.Select(a => a.ExerciseId).Should().Equal(new[] { 2, 1, 3 }, "ties keep the order the answers were saved in");
        answers.Should().OnlyContain(a => a.Timestamp.Kind == DateTimeKind.Utc);
        answers[0].Timestamp.Ticks.Should().Be(Start.Ticks);
    }

    [Fact]
    public async Task TheAnswersAreReadOnce()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.Add(Answer(exerciseId: 1, Start));
        await db.SaveChangesAsync();
        var history = new PracticeHistory(db);

        var first = await history.GetAsync(UserId);

        (await history.GetAsync(UserId)).Should().BeSameAs(first);
        (await history.GetAsync("someone-else")).Should().BeEmpty();
    }

    [Fact]
    public async Task SavingAnAnswer_ReadsTheAnswersAgain()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.Add(Answer(exerciseId: 1, Start));
        await db.SaveChangesAsync();
        var history = new PracticeHistory(db);
        var before = await history.GetAsync(UserId);

        db.ScoreSnapshots.Add(Answer(exerciseId: 2, Start.AddMinutes(1)));
        await db.SaveChangesAsync();
        var added = await history.GetAsync(UserId);

        db.ScoreSnapshots.Remove(await db.ScoreSnapshots.SingleAsync(s => s.ExerciseId == 1));
        db.SaveChanges();
        var removed = await history.GetAsync(UserId);

        before.Select(a => a.ExerciseId).Should().Equal(1);
        added.Select(a => a.ExerciseId).Should().Equal(1, 2);
        removed.Select(a => a.ExerciseId).Should().Equal(2);
    }

    [Fact]
    public async Task SavingOtherData_KeepsTheAnswers()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.Add(Answer(exerciseId: 1, Start));
        await db.SaveChangesAsync();
        var history = new PracticeHistory(db);
        var before = await history.GetAsync(UserId);

        // Awarding a badge saves between the XP read and the learning path read.
        db.Badges.AddRange(SeedData.BadgeSeed());
        db.BadgesEarned.Add(new BadgesEarned { UserId = UserId, BadgeKey = BadgeKeys.FirstSession, EarnedDate = Start });
        await db.SaveChangesAsync();

        (await history.GetAsync(UserId)).Should().BeSameAs(before);
    }

    [Fact]
    public async Task Answers_KeepTheFiltersTheyWerePlayedWith()
    {
        await using var db = NewDatabase();
        db.ScoreSnapshots.AddRange(
            Answer(exerciseId: 1, Start, filterJson: """{"keySelect":"D4"}"""),
            Answer(exerciseId: 2, Start.AddMinutes(1)));
        await db.SaveChangesAsync();

        var answers = await new PracticeHistory(db).GetAsync(UserId);

        answers.Select(a => a.FilterJson).Should().Equal("""{"keySelect":"D4"}""", null);
    }

    private static ApplicationDbContext NewDatabase() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"practice-history-{Guid.NewGuid():N}")
        .Options);

    private static ScoreSnapshot Answer(int exerciseId, DateTime timestamp, string userId = UserId, string? filterJson = null) =>
        new() { UserId = userId, ExerciseId = exerciseId, IsCorrect = true, Timestamp = timestamp, FilterJson = filterJson };
}
