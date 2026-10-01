using System.Data.Common;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>The learning path on SQL Server, and the answer history it shares with XP and badges.</summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class LearningPathSqlTests
{
    private readonly RealSqlServerFixture _fixture;

    public LearningPathSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task Progress_CountsThePlayersAnswersInOrder()
    {
        var (userId, ids) = await CreatePlayerAsync();
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var at = DateTime.UtcNow.AddMinutes(-5);
        var answers = Enumerable.Range(0, 8).Select(i => ("HigherOrLower", true, at.AddSeconds(i)))
            .Append(("GuessInterval", false, at.AddSeconds(10)))
            .Append(("GuessInterval", true, at.AddSeconds(11)));
        foreach (var (exercise, correct, timestamp) in answers)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = userId, ExerciseId = ids[exercise], IsCorrect = correct, Timestamp = timestamp });
        }
        await db.SaveChangesAsync();

        var progress = await new LearningPathService(db, new PracticeHistory(db)).GetProgressAsync(userId);

        progress.TotalSteps.Should().Be(LearningPathCatalog.Steps.Count);
        progress.CompletedSteps.Should().Be(1);
        progress.Current.Should().BeEquivalentTo(new { Exercise = "GuessInterval", Correct = 1, Answered = 2 });
        progress.JustCompleted.Should().BeNull();
    }

    // What ValidateExercise does: save the answer, then XP and badges, then the path.
    [RealSqlFact]
    public async Task AnAnswer_ReadsTheHistoryOnce_ForTheRewardsAndThePath()
    {
        var (userId, ids) = await CreatePlayerAsync();
        var reads = new ScoreSnapshotReads();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .UseApplicationServiceProvider(_fixture.Services)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(reads)
            .Options);
        var history = new PracticeHistory(db);
        var gamification = new GamificationService(
            db,
            history,
            _fixture.Services.GetRequiredService<IStringLocalizer<SharedResources>>(),
            NullLogger<GamificationService>.Instance,
            TimeProvider.System);
        var learningPath = new LearningPathService(db, history);

        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = userId, ExerciseId = ids["HigherOrLower"], IsCorrect = true, Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var rewards = await gamification.RecordAttemptAsync(userId, isCorrect: true, TimeZoneInfo.Utc);
        var progress = await learningPath.GetProgressAsync(userId);

        reads.Count.Should().Be(1, "awarding the badge saves in between, but no answer");
        rewards.NewBadges.Select(b => b.Key).Should().Contain(BadgeKeys.FirstSession);
        rewards.Progress.TotalAnswers.Should().Be(1);
        progress.Current.Should().BeEquivalentTo(new { Exercise = "HigherOrLower", Correct = 1, Answered = 1 });

        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = userId, ExerciseId = ids["HigherOrLower"], IsCorrect = false, Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        progress = await learningPath.GetProgressAsync(userId);

        reads.Count.Should().Be(2, "saving an answer discards the copy");
        progress.Current.Should().BeEquivalentTo(new { Exercise = "HigherOrLower", Correct = 1, Answered = 2 });
    }

    private async Task<(string UserId, Dictionary<string, int> ExerciseIds)> CreatePlayerAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
        var email = $"learning-path.{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, await db.Exercises.ToDictionaryAsync(e => e.Name, e => e.ExerciseId));
    }

    /// <summary>Counts the queries that read ScoreSnapshots.</summary>
    private sealed class ScoreSnapshotReads : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [ScoreSnapshots]", StringComparison.Ordinal)) Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
