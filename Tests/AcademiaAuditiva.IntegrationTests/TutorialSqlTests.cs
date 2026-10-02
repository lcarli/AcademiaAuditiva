using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Tour storage on SQL Server: the unique (UserId, TutorialKey) index keeps one row
/// when several tabs close the same tour at once.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class TutorialSqlTests
{
    private readonly RealSqlServerFixture _fixture;

    public TutorialSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task ParallelCloses_KeepOneRow_FinishedIfAnyTabFinished()
    {
        var userId = await CreateUserAsync();

        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closes = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var tutorials = scope.ServiceProvider.GetRequiredService<ITutorialService>();
            await go.Task;
            await tutorials.MarkSeenAsync(userId, TutorialCatalog.Exercise, finished: i == 7);
        })).ToList();
        go.SetResult();
        await Task.WhenAll(closes);

        await using var db = _fixture.CreateContext();
        var row = await db.UserTutorials.SingleAsync(t => t.UserId == userId);
        row.TutorialKey.Should().Be(TutorialCatalog.Exercise);
        row.Finished.Should().BeTrue("one tab reached the last step");
    }

    private async Task<string> CreateUserAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var email = $"tutorial.{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
