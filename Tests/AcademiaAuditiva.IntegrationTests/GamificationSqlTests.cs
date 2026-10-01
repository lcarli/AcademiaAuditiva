using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Badge storage on SQL Server: the seed keeps the Badges table in sync with the
/// catalog, and the unique (UserId, BadgeKey) index lets parallel answers award a
/// badge only once.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class GamificationSqlTests
{
    private readonly RealSqlServerFixture _fixture;

    public GamificationSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task SeedExercises_AddsMissingBadges_AndKeepsExistingTexts()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
        var originalTitle = await db.Badges.Where(b => b.BadgeKey == BadgeKeys.FirstSession).Select(b => b.Title).SingleAsync();
        try
        {
            // No player can earn "explorer" yet, so no BadgesEarned row blocks the delete.
            await db.Badges.Where(b => b.BadgeKey == BadgeKeys.Explorer).ExecuteDeleteAsync();
            await db.Badges.Where(b => b.BadgeKey == BadgeKeys.FirstSession)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Title, "Edited title"));
            // The first seed may still track the deleted row, as a running app's context would not.
            db.ChangeTracker.Clear();

            SeedData.SeedExercises(db);

            var badges = await db.Badges.AsNoTracking().ToListAsync();
            badges.Select(b => b.BadgeKey).Should().BeEquivalentTo(BadgeCatalog.All.Select(b => b.Key));
            badges.Single(b => b.BadgeKey == BadgeKeys.FirstSession).Title.Should().Be("Edited title");
        }
        finally
        {
            await db.Badges.Where(b => b.BadgeKey == BadgeKeys.FirstSession)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Title, originalTitle));
        }
    }

    [RealSqlFact]
    public async Task ParallelAnswers_AwardEachBadgeOnce()
    {
        var userId = await CreatePlayerWithOneAnswerAsync();

        // A double submit or a second tab: every request evaluates the same history.
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var gamification = scope.ServiceProvider.GetRequiredService<IGamificationService>();
            await go.Task;
            return await gamification.RecordAttemptAsync(userId, isCorrect: true, TimeZoneInfo.Utc);
        })).ToList();
        go.SetResult();
        var rewards = await Task.WhenAll(answers);

        rewards.Where(r => r.NewBadges.Any(b => b.Key == BadgeKeys.FirstSession)).Should().ContainSingle();
        rewards.Should().AllSatisfy(r => r.Progress.Xp.Should().Be(Leveling.XpPerCorrectAnswer + Leveling.XpPerBadge));
        (await CountEarnedAsync(userId)).Should().Be(1);
    }

    // The interleaving above depends on timing; this forces the losing side of the race.
    [RealSqlFact]
    public async Task AwardConflict_KeepsTheBadgeAParallelAnswerSaved()
    {
        var userId = await CreatePlayerWithOneAnswerAsync();
        var parallelAnswer = new AwardBadgeFirstInterceptor(_fixture.ConnectionString);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .UseApplicationServiceProvider(_fixture.Services)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(parallelAnswer)
            .Options);
        var service = new GamificationService(
            db,
            _fixture.Services.GetRequiredService<IStringLocalizer<SharedResources>>(),
            NullLogger<GamificationService>.Instance,
            TimeProvider.System);

        var rewards = await service.RecordAttemptAsync(userId, isCorrect: true, TimeZoneInfo.Utc);

        parallelAnswer.Awarded.Should().Equal(BadgeKeys.FirstSession);
        rewards.NewBadges.Should().BeEmpty("the parallel answer awarded the badge");
        rewards.XpGained.Should().Be(Leveling.XpPerCorrectAnswer);
        rewards.Progress.Xp.Should().Be(Leveling.XpPerCorrectAnswer + Leveling.XpPerBadge, "the badge is reloaded");
        (await CountEarnedAsync(userId)).Should().Be(1);
    }

    private async Task<string> CreatePlayerWithOneAnswerAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
        var email = $"gamification.{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var exerciseId = await db.Exercises.Select(e => e.ExerciseId).FirstAsync();
        db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = user.Id, ExerciseId = exerciseId, IsCorrect = true, Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> CountEarnedAsync(string userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BadgesEarned.CountAsync(b => b.UserId == userId);
    }

    /// <summary>Saves the first badge award from another connection right before the context does.</summary>
    private sealed class AwardBadgeFirstInterceptor(string connectionString) : SaveChangesInterceptor
    {
        public List<string> Awarded { get; } = [];

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var award = eventData.Context!.ChangeTracker.Entries<BadgesEarned>()
                .FirstOrDefault(e => e.State == EntityState.Added)?.Entity;
            if (award is not null && Awarded.Count == 0)
            {
                Awarded.Add(award.BadgeKey);
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO [BadgesEarned] ([UserId], [BadgeKey], [EarnedDate], [IsNew])
                    VALUES (@userId, @badgeKey, SYSUTCDATETIME(), 1)
                    """;
                command.Parameters.AddWithValue("@userId", award.UserId);
                command.Parameters.AddWithValue("@badgeKey", award.BadgeKey);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
