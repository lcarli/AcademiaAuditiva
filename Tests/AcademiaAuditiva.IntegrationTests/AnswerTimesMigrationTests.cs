using AcademiaAuditiva.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// RecomputeAnswerTimes turns the running totals answers used to save (time since the
/// exercise page was opened) into each answer's own time, capped at five minutes, in
/// Scores and ScoreSnapshots. It runs on its own database so it can start from the
/// schema before the migration.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class AnswerTimesMigrationTests
{
    private const string PreviousMigration = "20261005133237_AddScoreSnapshotFilterJson";
    private static readonly DateTime Start = new(2026, 9, 1, 10, 0, 0);

    // (user, exercise, seconds after Start the answer was saved, time saved, time after the migration)
    private static readonly (int User, int Exercise, int At, int Saved, int Expected)[] Answers =
    [
        (1, 1, 10, 10, 10),      // first answer: kept
        (1, 1, 25, 25, 15),      // same visit: 15 s after the previous answer
        (1, 1, 27, 27, 2),
        (1, 1, 1027, 1027, 300), // a round left open: capped
        (1, 1, 1130, 30, 30),    // the count started again: a new visit
        (1, 1, 1140, 41, 11),    // same visit, a second of lag
        (1, 1, 2000, 400, 300),  // not 359 s after the previous answer: a new visit, capped
        (1, 2, 40, 40, 40),      // another exercise counts on its own
        (2, 1, 26, 26, 26),      // so does another user
        (2, 2, 50, -3, 0),
    ];

    private readonly RealSqlServerFixture _fixture;

    public AnswerTimesMigrationTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task RecomputeAnswerTimes_KeepsEachAnswersOwnTime()
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

            // Plain SQL against the schema before the migration, so later model changes don't break this setup.
            string[] users = [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()];
            foreach (var userId in users)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [AspNetUsers] ([Id], [Discriminator], [FirstName], [LastName], [UserName], [NormalizedUserName],
                        [Email], [NormalizedEmail], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
                    VALUES ({userId}, N'ApplicationUser', N'Answer', N'Times', {userId}, UPPER({userId}),
                        {userId + "@example.test"}, UPPER({userId + "@example.test"}), 1, 0, 0, 0, 0);
                    """);
            }

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO [DifficultyLevels] ([Name], [DisplayName]) VALUES (N'AnswerTimes', N'AnswerTimes');
                INSERT INTO [ExerciseCategories] ([Name], [DisplayName]) VALUES (N'AnswerTimes', N'AnswerTimes');
                INSERT INTO [ExerciseTypes] ([Name], [DisplayName]) VALUES (N'AnswerTimes', N'AnswerTimes');
                INSERT INTO [Exercises] ([Name], [Description], [DifficultyLevelId], [ExerciseCategoryId], [ExerciseTypeId])
                SELECT [Name], N'AnswerTimes',
                    (SELECT [Id] FROM [DifficultyLevels] WHERE [Name] = N'AnswerTimes'),
                    (SELECT [Id] FROM [ExerciseCategories] WHERE [Name] = N'AnswerTimes'),
                    (SELECT [Id] FROM [ExerciseTypes] WHERE [Name] = N'AnswerTimes')
                FROM (VALUES (N'AnswerTimesA'), (N'AnswerTimesB')) AS [New] ([Name]);
                """);
            int[] exercises =
            [
                await ExerciseIdAsync(db, "AnswerTimesA"),
                await ExerciseIdAsync(db, "AnswerTimesB"),
            ];

            foreach (var answer in Answers)
            {
                var userId = users[answer.User - 1];
                var exerciseId = exercises[answer.Exercise - 1];
                var at = Start.AddSeconds(answer.At);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [Scores] ([UserId], [ExerciseId], [CorrectCount], [ErrorCount], [BestScore], [TimeSpentSeconds], [Timestamp], [DateCreated])
                    VALUES ({userId}, {exerciseId}, 1, 0, 1, {answer.Saved}, {at}, {at});
                    INSERT INTO [ScoreSnapshots] ([UserId], [ExerciseId], [IsCorrect], [TimeSpentSeconds], [Timestamp])
                    VALUES ({userId}, {exerciseId}, 1, {answer.Saved}, {at});
                    """);
            }

            await migrator.MigrateAsync();

            var expected = Answers.OrderBy(a => a.At).Select(a => a.Expected).ToList();
            (await db.Scores.AsNoTracking().OrderBy(s => s.Timestamp).Select(s => s.TimeSpentSeconds).ToListAsync())
                .Should().Equal(expected);
            (await db.ScoreSnapshots.AsNoTracking().OrderBy(s => s.Timestamp).Select(s => s.TimeSpentSeconds).ToListAsync())
                .Should().Equal(expected);

            await migrator.MigrateAsync(PreviousMigration);
            (await db.Scores.AsNoTracking().SumAsync(s => s.TimeSpentSeconds))
                .Should().Be(expected.Sum(), "the running totals can't be restored, so reverting leaves the times as they are");
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

    private static async Task<int> ExerciseIdAsync(ApplicationDbContext db, string name) =>
        await db.Database.SqlQuery<int>($"SELECT [ExerciseId] AS [Value] FROM [Exercises] WHERE [Name] = {name}").SingleAsync();

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
