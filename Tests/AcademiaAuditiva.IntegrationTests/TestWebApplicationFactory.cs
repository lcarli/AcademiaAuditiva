using AcademiaAuditiva.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Boots the real <c>Program.cs</c> pipeline against an in-memory EF
/// store. Program.cs short-circuits the migration/seed/bootstrap block
/// when the environment is "Testing", so this factory just has to:
///
/// 1. Set <c>ASPNETCORE_ENVIRONMENT</c> to Testing.
/// 2. Replace the SQL-Server-backed <see cref="ApplicationDbContext"/>
///    registration with an InMemory one (each test gets its own DB).
/// 3. Provide a placeholder DefaultConnection so the configuration check
///    in Program.cs doesn't throw before Configure runs.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = $"AAIntegration_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=test;Database=test;",
                ["AzureKeyVault:Url"] = "",
                ["ApplicationInsights:ConnectionString"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop any pre-registered DbContext / DbContextOptions so we can
            // swap the provider without conflicting registrations.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(ApplicationDbContext)).ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(opt =>
                opt.UseInMemoryDatabase(DatabaseName));

            // Make sure the schema exists before any test request runs.
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ctx.Database.EnsureCreated();
        });
    }
}
