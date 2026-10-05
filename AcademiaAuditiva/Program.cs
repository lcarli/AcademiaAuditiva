using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using AcademiaAuditiva.Services.Tutorials;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using Serilog;
using System.Text.Json;

// Bootstrap logger — captures startup errors before host is built.
// Guard against re-initialization: WebApplicationFactory invokes the entry
// point multiple times per process during integration tests, and Serilog's
// Log.Logger cannot be re-assigned once a non-bootstrap logger has been
// frozen by UseSerilog().
if (Log.Logger.GetType().FullName == "Serilog.Core.Pipeline.SilentLogger")
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .CreateBootstrapLogger();
}

try
{
var builder = WebApplication.CreateBuilder(args);
var appVersion = builder.Configuration["APP_VERSION"] ?? builder.Configuration["AppVersion"] ?? "dev";

// Wire Azure Key Vault BEFORE reading any configuration so KV-backed values
// (Facebook, SMTP, Admin, ConnectionStrings) are available below. In Azure
// the Container App sets AzureKeyVault__Url and the app reads the vault with
// its managed identity, through the vault's private endpoint. Vault values
// override environment variables.
var keyVaultUrl = builder.Configuration["AzureKeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
{
    var credential = builder.Environment.IsDevelopment()
        ? new DefaultAzureCredential()
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = builder.Configuration["ManagedIdentityClientId"]
        });

    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), credential);
}

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    // EF's startup migration safety check can flag custom SQL-only
    // migrations (dbo.AppCache) even when the model snapshot is current.
    // MigrationsTests and real SQL tests still assert HasPendingModelChanges.
    options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        // Wrong passwords and two-factor codes count toward a lockout: after five
        // in a row the account can't sign in for 15 minutes, or until its password
        // is reset. An admin's lock (AdminLock) lasts until an admin unlocks it.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<LockoutAwareSignInManager>();

// Sign-in cookies are checked against the account every minute instead of
// Identity's default 30: a session ends soon after an admin locks (see
// LockoutAwareSignInManager) or deletes the account, and role changes apply quickly.
// A page that changes the security stamp of the signed-in account must call
// RefreshSignInAsync, or that session ends at its next check, a minute later.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddScoped<IdentityBootstrapper>();

// Authorization policies for Admin/Teacher/Student areas.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AcademiaAuditiva.Models.RoleNames.Admin,
        policy => policy.RequireRole(AcademiaAuditiva.Models.RoleNames.Admin));
    options.AddPolicy(AcademiaAuditiva.Models.RoleNames.Teacher,
        policy => policy.RequireRole(AcademiaAuditiva.Models.RoleNames.Admin,
                                      AcademiaAuditiva.Models.RoleNames.Teacher));
    options.AddPolicy(AcademiaAuditiva.Models.RoleNames.Student,
        policy => policy.RequireRole(AcademiaAuditiva.Models.RoleNames.Admin,
                                      AcademiaAuditiva.Models.RoleNames.Teacher,
                                      AcademiaAuditiva.Models.RoleNames.Student));
});

// Azure Monitor OpenTelemetry distro. Application Insights is still the
// backend, but local/dev/test runs stay silent unless a connection string is
// configured. Serilog below writes through MEL providers so app logs share
// the same OTel pipeline without a second AI-specific sink.
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString) && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsConnectionString;
        });
}

// Serilog — structured events routed once through Microsoft.Extensions.Logging
// providers. The default Console provider keeps stdout logs, and Azure Monitor
// OpenTelemetry exports them when configured.
// Replaces the default ASP.NET Core logger so we get structured logs end-to-end.
// Skipped under the Testing environment because WebApplicationFactory invokes
// the entry point repeatedly per test and Serilog's static logger cannot be
// re-frozen, leading to "The logger is already frozen" failures.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithProperty("Application", "AcademiaAuditiva")
           .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
           .Enrich.WithProperty("AppVersion", appVersion);
    }, writeToProviders: true);

    // Register after Azure Monitor OpenTelemetry so its exporter hosted
    // service starts first. This keeps migration/seed/bootstrap logs and
    // startup failures exportable while still failing fast before Kestrel
    // serves traffic if database bootstrap fails.
    builder.Services.AddHostedService<StartupBootstrapHostedService>();
}

// Health checks: liveness ("is the process up?") and readiness ("can it
// reach SQL, and did the image ship every instrument sample?"). The Bicep
// startup/liveness probes hit /health/live and the readiness probe
// /health/ready, so a revision only gets traffic once it is ready.
var hcBuilder = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(connectionString))
{
    hcBuilder.AddSqlServer(
        connectionString: connectionString,
        name: "sql",
        tags: new[] { "ready" });
}

builder.Services.AddSingleton(new AcademiaAuditiva.Services.Audio.BundledSamples(
    Path.Combine(builder.Environment.ContentRootPath, "Audio", "Instruments")));
builder.Services.AddSingleton<AcademiaAuditiva.Services.Audio.InstrumentSamplesHealthCheck>();
hcBuilder.AddCheck<AcademiaAuditiva.Services.Audio.InstrumentSamplesHealthCheck>(
    "instrument-samples",
    tags: new[] { "ready" });

builder.Services.AddLocalization();


builder.Services.AddMvc()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResources));
    });

builder.Services.AddOptions<MvcOptions>()
    .Configure<IStringLocalizer<SharedResources>>((options, localizer) =>
    {
        var messages = options.ModelBindingMessageProvider;
        messages.SetValueMustNotBeNullAccessor(field => localizer["Validation.Required", field]);
        messages.SetMissingBindRequiredValueAccessor(field => localizer["Validation.Required", field]);
        messages.SetMissingKeyOrValueAccessor(() => localizer["Validation.MissingKeyOrValue"]);
        messages.SetValueIsInvalidAccessor(value => localizer["Validation.InvalidValue", value]);
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => localizer["Validation.InvalidAttemptedValue", value, field]);
        messages.SetUnknownValueIsInvalidAccessor(field => localizer["Validation.InvalidUnknownValue", field]);
    });

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("fr-CA"),
        new CultureInfo("en-US"),
        new CultureInfo("pt-BR")
    };

    options.DefaultRequestCulture = new RequestCulture(culture: "en-US", uiCulture: "en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

//Inject EmailSender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddTransient<IEmailMessageSender, EmailSender>();
// Identity's default UI would otherwise fall back to its no-op sender.
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<AcademiaAuditiva.Services.Email.EmailComposer>();

//Inject AnalyticsService
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IMusicTheoryService, MusicTheoryServiceAdapter>();

// Exercise validators (Strategy pattern). Each implementation owns the
// JSON shape for one exercise; the registry resolves by Exercise.Name.
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessNoteValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessChordsValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessIntervalValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessMissingNoteValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessFullIntervalValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessFunctionValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessQualityValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.HigherOrLowerValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessScaleTypeValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessGreekModeValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessCadenceValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.GuessInversionValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.CompleteScaleValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.CompleteChordValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.TransposeScaleValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.MelodicDictationValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.RhythmDictationValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.IntervalMelodicoValidator>();
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.SolfegeMelodyValidator>();
builder.Services.AddSingleton<IExerciseValidatorRegistry, AcademiaAuditiva.Services.ExerciseValidators.ExerciseValidatorRegistry>();

//Inject UserReportService
builder.Services.AddScoped<UserReportService>();
builder.Services.AddScoped<PersonalDataService>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<PracticeHistory>();
builder.Services.AddScoped<IGamificationService, GamificationService>();
builder.Services.AddScoped<ILearningPathService, LearningPathService>();
builder.Services.AddScoped<IDailyChallengeService, DailyChallengeService>();
builder.Services.AddScoped<ITutorialService, TutorialService>();


// Facebook login (external auth). Credentials come from configuration:
//   Facebook:AppId / Facebook:AppSecret
// In Azure these are injected from Key Vault as Facebook__AppId / Facebook__AppSecret.
// Locally use `dotnet user-secrets` or appsettings.Development.Local.json (gitignored).
var fbAppId = builder.Configuration["Facebook:AppId"];
var fbAppSecret = builder.Configuration["Facebook:AppSecret"];
if (!string.IsNullOrWhiteSpace(fbAppId) && !string.IsNullOrWhiteSpace(fbAppSecret))
{
    builder.Services.AddAuthentication()
        .AddFacebook(facebookOptions =>
        {
            facebookOptions.AppId = fbAppId;
            facebookOptions.AppSecret = fbAppSecret;
            facebookOptions.AccessDeniedPath = "/AccessDeniedPathInfo";
        });
}

builder.Services.AddControllersWithViews(options =>
{
    // Global anti-forgery enforcement on every unsafe HTTP method (POST,
    // PUT, DELETE, PATCH). Combined with the AntiforgeryOptions below,
    // this protects every authenticated mutating endpoint — including the
    // JSON Exercise RequestPlay/ValidateExercise actions — from CSRF without requiring
    // each controller method to opt in via [ValidateAntiForgeryToken].
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// Expose the anti-forgery token to JS via a custom request header so the
// fetch() calls in wwwroot/js/Exercises/* can attach it. The bootstrap
// script in _Layout.cshtml wraps window.fetch and injects this header for
// same-origin POSTs, so existing JSON endpoints keep working.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Distributed cache for short-lived per-user state (exercise expected
// answers and audio tokens). Production stores it in SQL Server so multiple
// replicas share round state; Testing keeps the in-memory provider to avoid
// external dependencies in ordinary integration tests.
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddDistributedSqlServerCache(options =>
    {
        options.ConnectionString = connectionString;
        options.SchemaName = "dbo";
        options.TableName = "AppCache";
    });
}

// Audio anti-cheat: opaque per-round tokens replace plaintext note names
// in the RequestPlay → /audio/token → ValidateExercise flow. The token
// service owns cache shapes; the mixer composes per-question audio so
// the front never sees note identity.
builder.Services.AddSingleton<AcademiaAuditiva.Interfaces.IAudioTokenService,
    AcademiaAuditiva.Services.Audio.AudioTokenService>();

// Single BlobServiceClient shared by AudioController (read piano-audio)
// and AudioMixerService (read piano-audio + write piano-audio-mixed; the
// samples of the other instruments ship with the app, see BundledSamples).
// Both containers live in the same storage account; one client with
// the app's managed identity is enough.
//
// Local development: Storage:ConnectionString (set by
// scripts/local-audio.ps1 to "UseDevelopmentStorage=true", i.e. Azurite)
// takes precedence over the managed-identity endpoint.
//
// We always register the client and the mixer so the rest of the
// graph can resolve at startup. When neither setting is present
// (local dev without storage), the underlying calls just fail at
// request time — same fail-mode as before.
{
    var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
    var storageConnectionString = builder.Configuration["Storage:ConnectionString"];
    var miClientId = builder.Configuration["ManagedIdentityClientId"];
    var azureCredential = string.IsNullOrWhiteSpace(miClientId)
        ? new Azure.Identity.DefaultAzureCredential()
        : new Azure.Identity.DefaultAzureCredential(new Azure.Identity.DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = miClientId
        });
    var endpointUri = !string.IsNullOrWhiteSpace(blobEndpoint)
        ? new Uri(blobEndpoint)
        : new Uri("https://placeholder.invalid/");
    builder.Services.AddSingleton(!string.IsNullOrWhiteSpace(storageConnectionString)
        ? new Azure.Storage.Blobs.BlobServiceClient(storageConnectionString)
        : new Azure.Storage.Blobs.BlobServiceClient(endpointUri, azureCredential));
    builder.Services.AddSingleton<AcademiaAuditiva.Interfaces.IAudioMixerService,
        AcademiaAuditiva.Services.Audio.AudioMixerService>();

    // Data Protection keys encrypt the auth cookie and antiforgery tokens.
    // The default key ring lives inside the container, so every deploy
    // logged everyone out and replicas rejected each other's cookies.
    // In Azure the ring is shared in blob storage and wrapped by a Key
    // Vault key; locally (no DataProtection:BlobUri) the default file
    // system ring is kept.
    var dataProtection = builder.Services.AddDataProtection()
        .SetApplicationName("AcademiaAuditiva");
    var dpBlobUri = builder.Configuration["DataProtection:BlobUri"];
    if (!string.IsNullOrWhiteSpace(dpBlobUri))
    {
        dataProtection.PersistKeysToAzureBlobStorage(new Uri(dpBlobUri), azureCredential);

        var dpKeyIdentifier = builder.Configuration["DataProtection:KeyIdentifier"];
        if (!string.IsNullOrWhiteSpace(dpKeyIdentifier))
            dataProtection.ProtectKeysWithAzureKeyVault(new Uri(dpKeyIdentifier), azureCredential);
    }
}

// Container Apps terminates TLS at its Envoy ingress and forwards plain
// HTTP. Honour X-Forwarded-For/Proto so Request.Scheme is https (email
// confirmation/reset links, OAuth redirect URIs, secure cookies, HSTS)
// and RemoteIpAddress is the real client. The ingress addresses aren't
// known up front, so the proxy allow-lists are cleared; the container
// is only reachable through the ingress.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Maps GenerateNoteForExercise output to a tokenizable mixer plan.
// Stateless — singleton is fine.
builder.Services.AddSingleton<AcademiaAuditiva.Services.Audio.ExercisePlaybackPlanner>();

// Turns Explore page choices into spelled notes and mixer plans. Pure — singleton.
builder.Services.AddSingleton<AcademiaAuditiva.Services.Audio.ExploreSoundBuilder>();

builder.Services.AddOptions<AccountFormsRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(AccountFormsRateLimitOptions.SectionName))
    .Validate(o => o.PermitLimit > 0 && o.Window > TimeSpan.Zero,
        $"{AccountFormsRateLimitOptions.SectionName} needs a PermitLimit and a Window above zero.")
    .ValidateOnStart();

// Rate limiting for the audio anti-cheat surface. Named policies, all
// partitioned per authenticated user (falls back to remote IP for
// anonymous traffic — those callers will fail authorization anyway, but
// partitioning prevents one IP from starving the bucket for everyone).
// The account forms have their own policy, per client IP.
//
// Limits are deliberately generous for legitimate practice (a student
// rarely fires more than a handful of rounds per minute) but tight
// enough that brute-force enumeration of token GUIDs is hopeless given
// the 32-hex search space + 15-min TTL.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("RequestPlay", httpContext =>
    {
        var key = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity!.Name ?? "anon"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("AudioToken", httpContext =>
    {
        var key = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity!.Name ?? "anon"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    // Explore plays a sound on every change of its controls, and the page
    // caches tokens it already has, so twice the round limit is plenty.
    options.AddPolicy("Explore", httpContext =>
    {
        var key = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity!.Name ?? "anon"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    // The sign-in, two-factor, registration and e-mail forms ([EnableRateLimiting]
    // on their page models), which check passwords and codes or send mail.
    options.AddPolicy<string, AccountFormsRateLimitPolicy>(AccountFormsRateLimitPolicy.Name);
});


var app = builder.Build();

// Must run first so every later middleware sees the original scheme/client IP.
app.UseForwardedHeaders();

// Localization — single source of truth: the RequestLocalizationOptions
// configured above (default en-US, supports fr-CA / en-US / pt-BR).
// Pulling from IOptions ensures middleware and DI agree on the same set.
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Only Kestrel's own HTTPS endpoint (local "https" launch profile) needs this.
// In Azure Container Apps the ingress terminates TLS and redirects http -> https;
// the container listens on plain HTTP, so the middleware would never find an
// HTTPS port and would just log a warning.
var serverUrls = app.Configuration[WebHostDefaults.ServerUrlsKey] ?? string.Empty;
if (serverUrls.Contains("https://", StringComparison.OrdinalIgnoreCase)
    || !string.IsNullOrEmpty(app.Configuration[WebHostDefaults.HttpsPortsKey])
    || !string.IsNullOrEmpty(app.Configuration["HTTPS_PORT"]))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseSerilogRequestLogging();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Health endpoints. /health/live always returns 200 if the host is up.
// /health/ready returns 200 only if every check tagged "ready" passes.
static Task WriteHealthResponse(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    context.Response.Headers["X-App-Version"] = context.RequestServices.GetRequiredService<IConfiguration>()["APP_VERSION"] ?? "dev";
    return JsonSerializer.SerializeAsync(context.Response.Body, new
    {
        status = report.Status.ToString(),
        version = context.Response.Headers["X-App-Version"].ToString()
    });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException
    // WebApplicationFactory throws an internal HostingListener exception
    // during integration tests to stop the entry-point right after Build();
    // letting it bubble out is required for the test host to capture the
    // built IHost. The exception type is internal, so match by full name.
    && !(ex.GetType().FullName?.Contains("HostingListener") ?? false)
    && !(ex.GetType().FullName?.Contains("StopTheHost") ?? false))
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the implicit Program class to WebApplicationFactory<Program>
// in the integration test project.
public partial class Program { }

internal sealed class StartupBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<StartupBootstrapHostedService> _logger;

    public StartupBootstrapHostedService(
        IServiceProvider services,
        ILogger<StartupBootstrapHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting database migration, exercise seed and admin bootstrap.");

            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync(cancellationToken);

            SeedData.SeedExercises(context);

            var bootstrapper = scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>();
            await bootstrapper.RunAsync();

            _logger.LogInformation("Database migration, exercise seed and admin bootstrap completed.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Database migration, exercise seed or admin bootstrap failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
