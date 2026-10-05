using System.Runtime.InteropServices;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace AcademiaAuditiva.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class RealSqlServerCollection : ICollectionFixture<RealSqlServerFixture>
{
    public const string Name = "Real SQL Server";
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RealSqlFactAttribute : FactAttribute
{
    public RealSqlFactAttribute()
    {
        var hasConnection = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AA_TEST_SQL_CONNECTION"));
        var inCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        if (!hasConnection && !inCi)
        {
            Skip = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "Real SQL tests skipped: SQL Server container images do not run on local ARM64; set AA_TEST_SQL_CONNECTION to an existing SQL Server."
                : "Real SQL tests skipped: set AA_TEST_SQL_CONNECTION or run in CI where Testcontainers starts SQL Server.";
        }
    }
}

public sealed class RealSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private string? _adminConnectionString;
    private ServiceProvider? _services;

    public string DatabaseName { get; } = $"AA_Test_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = string.Empty;
    public FailCommitAfterUserDelete CommitFailure { get; } = new();
    public IServiceProvider Services => _services ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("AA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }

        ConnectionString = WithDatabase(_adminConnectionString, DatabaseName);
        await ExecuteMasterAsync($"CREATE DATABASE [{DatabaseName}]");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(ConnectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            options.AddInterceptors(CommitFailure);
        });
        // Same store options as AddDefaultIdentity in Program.cs: MaxLengthForKeys
        // sizes the Identity login/token key columns, so it is part of the model.
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<PersonalDataService>();
        services.AddSingleton<IAnalyticsService, NoopAnalyticsService>();
        services.AddLocalization();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PracticeHistory>();
        services.AddScoped<IGamificationService, GamificationService>();
        services.AddScoped<ITutorialService, TutorialService>();
        services.AddScoped<UserReportService>();
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    public async Task DisposeAsync()
    {
        _services?.Dispose();

        if (!string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            await ExecuteMasterAsync($"""
IF DB_ID(N'{DatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{DatabaseName}];
END
""");
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public ApplicationDbContext CreateContext()
    {
        // EF caches one model per context type, so this context must see the
        // same Identity store options as the ones resolved from Services.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .UseApplicationServiceProvider(Services)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private async Task ExecuteMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(WithDatabase(_adminConnectionString!, "master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = database,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }

    private sealed class NoopAnalyticsService : IAnalyticsService
    {
        public Task SaveAttemptAsync(ExerciseAttemptLog log) => Task.CompletedTask;
        public Task<List<ExerciseAttemptLog>> GetAttemptsAsync(string userId, string? exercise = null) => Task.FromResult(new List<ExerciseAttemptLog>());
        public Task DeleteAttemptsAsync(string userId) => Task.CompletedTask;
    }
}
