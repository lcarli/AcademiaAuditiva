using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// MapBadgesEarnedBadgeKey makes BadgesEarned.BadgeKey the foreign key to Badge
/// (replacing the shadow BadgeKey1 column) and lets a user earn each badge once.
/// It runs on its own database so it can start from the schema before the migration.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class BadgesEarnedMigrationTests
{
    private const string PreviousMigration = "20261001131500_AddSqlServerDistributedCache";
    private readonly RealSqlServerFixture _fixture;

    public BadgesEarnedMigrationTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task MapBadgesEarnedBadgeKey_KeepsValidRows_AndEnforcesTheBadgeKey()
    {
        var database = $"AA_Test_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(_fixture.ConnectionString) { InitialCatalog = database }.ConnectionString;
        await ExecuteOnMasterAsync($"CREATE DATABASE [{database}]");
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            // Plain SQL against the schema before the migration (later model changes must not break
            // this setup), including rows that schema accepted: the same badge twice and an unknown badge.
            var userId = Guid.NewGuid().ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [AspNetUsers] ([Id], [Discriminator], [FirstName], [LastName], [UserName], [NormalizedUserName],
                    [Email], [NormalizedEmail], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
                VALUES ({userId}, N'ApplicationUser', N'Badge', N'Tester', N'badges@example.test', N'BADGES@EXAMPLE.TEST',
                    N'badges@example.test', N'BADGES@EXAMPLE.TEST', 1, 0, 0, 0, 0);

                INSERT INTO [Badges] ([BadgeKey], [Title], [Description]) VALUES
                    (N'first_session', N'First session', N'First session'),
                    (N'3_days', N'3 days', N'3 days in a row');

                INSERT INTO [BadgesEarned] ([UserId], [BadgeKey], [EarnedDate], [IsNew]) VALUES
                    ({userId}, N'first_session', '2026-01-01', 0),
                    ({userId}, N'first_session', '2026-02-01', 1),
                    ({userId}, N'no_such_badge', '2026-01-01', 1);
                """);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var earned = await db.BadgesEarned.AsNoTracking().Include(e => e.Badge).ToListAsync();
            earned.Should().ContainSingle();
            earned[0].BadgeKey.Should().Be("first_session");
            earned[0].EarnedDate.Should().Be(new DateTime(2026, 1, 1), "the first time the badge was earned is kept");
            earned[0].Badge.Title.Should().Be("First session", "the navigation now loads through BadgeKey");
            (await BadgeKey1ExistsAsync(db)).Should().BeFalse();

            db.BadgesEarned.Add(new BadgesEarned { UserId = userId, BadgeKey = "no_such_badge" });
            var unknown = () => db.SaveChangesAsync();
            (await unknown.Should().ThrowAsync<DbUpdateException>("the badge must exist"))
                .WithInnerException<SqlException>().Where(e => e.Number == 547);
            db.ChangeTracker.Clear();

            db.BadgesEarned.Add(new BadgesEarned { UserId = userId, BadgeKey = "first_session" });
            var repeat = () => db.SaveChangesAsync();
            (await repeat.Should().ThrowAsync<DbUpdateException>("a user earns each badge once"))
                .WithInnerException<SqlException>().Where(e => e.Number == 2601);
            db.ChangeTracker.Clear();

            db.BadgesEarned.Add(new BadgesEarned { UserId = userId, BadgeKey = "3_days" });
            await db.SaveChangesAsync();
            var deleteEarnedBadge = () => db.Database.ExecuteSqlRawAsync("DELETE FROM [Badges] WHERE [BadgeKey] = N'3_days'");
            (await deleteEarnedBadge.Should().ThrowAsync<SqlException>("an earned badge keeps its definition"))
                .Where(e => e.Number == 547);

            await migrator.MigrateAsync(PreviousMigration);
            (await BadgeKey1ExistsAsync(db)).Should().BeTrue("the migration can be reverted");
        }
        finally
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                SqlConnection.ClearPool(connection);
            }
            await ExecuteOnMasterAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    private static async Task<bool> BadgeKey1ExistsAsync(ApplicationDbContext db) =>
        await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID(N'BadgesEarned') AND name = N'BadgeKey1'")
            .SingleAsync() == 1;

    private async Task ExecuteOnMasterAsync(string sql)
    {
        var master = new SqlConnectionStringBuilder(_fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
