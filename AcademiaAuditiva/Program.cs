using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Events;

// Bootstrap logger — captures startup errors before host is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
var builder = WebApplication.CreateBuilder(args);

// Wire Azure Key Vault BEFORE reading any configuration so KV-backed values
// (Facebook, SMTP, ConnectionStrings) are available below.
// In Production the Container App injects KV secrets via env vars
// (Facebook__AppId, ConnectionStrings__DefaultConnection, ...) using
// keyVaultUrl secret references, so AddAzureKeyVault is only needed when
// AzureKeyVault:Url is explicitly configured.
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
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

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

// Application Insights (no-op locally when ConnectionString is empty).
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});

// Serilog — Console (always) + Application Insights (when configured).
// Replaces the default ASP.NET Core logger so we get structured logs end-to-end.
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "AcademiaAuditiva")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
       .WriteTo.Console(
           outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");

    var aiConn = ctx.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(aiConn))
    {
        var telemetryConfig = services.GetRequiredService<TelemetryConfiguration>();
        cfg.WriteTo.ApplicationInsights(telemetryConfig, TelemetryConverter.Traces);
    }
});

// Health checks: liveness ("is the process up?") and readiness ("can it
// reach SQL?"). The Bicep startup/liveness probes hit /health/live;
// /health/ready may be used by future external dependency dashboards.
var hcBuilder = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(connectionString))
{
    hcBuilder.AddSqlServer(
        connectionString: connectionString,
        name: "sql",
        tags: new[] { "ready" });
}

builder.Services.AddLocalization();


builder.Services.AddMvc()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

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
builder.Services.AddTransient<IEmailSender, EmailSender>();

//Inject AnalyticsService
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
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
builder.Services.AddSingleton<IExerciseValidator, AcademiaAuditiva.Services.ExerciseValidators.IntervalMelodicoValidator>();
builder.Services.AddSingleton<IExerciseValidatorRegistry, AcademiaAuditiva.Services.ExerciseValidators.ExerciseValidatorRegistry>();

//Inject UserReportService
builder.Services.AddScoped<UserReportService>();


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

builder.Services.AddControllersWithViews();

// Distributed cache for short-lived per-user state (e.g. the "expected
// answer" for an exercise round). Using IDistributedCache instead of
// HttpContext.Session means scale-out is safe: today the in-memory
// implementation matches single-replica production; swapping to a Redis
// instance is a one-line change (AddStackExchangeRedisCache).
builder.Services.AddDistributedMemoryCache();


var app = builder.Build();

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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Health endpoints. /health/live always returns 200 if the host is up.
// /health/ready returns 200 only if every check tagged "ready" passes.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

//seed
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();

    // Apply pending EF migrations on startup so a fresh DB (e.g. first
    // deploy in a new tenant) is brought up to schema before seed/bootstrap.
    context.Database.Migrate();

    SeedData.SeedExercises(context);

    var bootstrapper = services.GetRequiredService<IdentityBootstrapper>();
    await bootstrapper.RunAsync();
}

app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
