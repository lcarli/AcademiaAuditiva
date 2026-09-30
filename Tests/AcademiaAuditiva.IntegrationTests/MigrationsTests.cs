using AcademiaAuditiva.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Since EF Core 9, <c>Database.Migrate()</c> throws when the model has
/// changes that no migration captures. Program.cs migrates on startup, so
/// such drift would stop the app from booting in production. The context
/// comes from the app's own DI container because Identity store options
/// shape part of the model.
/// </summary>
public class MigrationsTests
{
    private sealed class SqlServerModelFactory : TestWebApplicationFactory
    {
        protected override bool UseInMemoryDatabase => false;
    }

    [Fact]
    public void Model_MatchesLatestMigrationSnapshot()
    {
        using var factory = new SqlServerModelFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Database.IsSqlServer().Should().BeTrue();
        context.Database.HasPendingModelChanges().Should().BeFalse(
            "model changes need a migration: dotnet ef migrations add <Name> --project AcademiaAuditiva");
    }
}
