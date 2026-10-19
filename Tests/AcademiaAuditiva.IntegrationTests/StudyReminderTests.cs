using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Push;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

public sealed class StudyReminderTests
{
    [Fact]
    public async Task ManifestAndWorkerArePublicAndDoNotCachePrivatePages()
    {
        await using var app = new TestWebApplicationFactory();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/manifest.webmanifest");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json");
        var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        manifest.GetProperty("display").GetString().Should().Be("standalone");
        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            var bytes = await client.GetByteArrayAsync(icon.GetProperty("src").GetString());
            bytes.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        }
        var worker = await client.GetStringAsync("/service-worker.js");
        worker.Should().Contain("showNotification").And.Contain("notificationclick");
        worker.Should().NotContain("caches.").And.NotContain("addEventListener('fetch'");
        (await client.GetStringAsync("/")).Should().Contain("manifest.webmanifest").And.Contain("/js/pwa.js");
    }

    [Fact]
    public async Task SettingsRequireAuthenticationAndAntiforgery()
    {
        await using var app = new TestWebApplicationFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymous = await client.PostAsJsonAsync("/StudyReminders/Subscribe", Input());
        anonymous.StatusCode.Should().Be(HttpStatusCode.Redirect);
        await using var signedIn = new SignedInWebApplicationFactory();
        using var authenticated = signedIn.CreateClient();
        (await authenticated.PostAsJsonAsync("/StudyReminders/Subscribe", Input())).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SaveUpdateAndRemoveOnlyTheCurrentUsersDevice()
    {
        await using var app = EnabledApp();
        using var client = await ClientAsync(app);
        var input = Input();
        (await client.PostAsJsonAsync("/StudyReminders/Subscribe", input)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/StudyReminders/Subscribe", input with { minuteOfDay = 1200 })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var state = await client.PostAsJsonAsync("/StudyReminders/Status", new { input.endpoint });
        var data = JsonDocument.Parse(await state.Content.ReadAsStringAsync()).RootElement;
        data.GetProperty("enabled").GetBoolean().Should().BeTrue();
        data.GetProperty("minuteOfDay").GetInt32().Should().Be(1200);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.StudyPushSubscriptions.SingleAsync();
            row.UserId.Should().Be(SignedInWebApplicationFactory.UserId);
            row.TimeZoneId.Should().Be("America/Sao_Paulo");
            db.Users.Add(new Microsoft.AspNetCore.Identity.IdentityUser { Id = "other" });
            db.StudyPushSubscriptions.Add(new StudyPushSubscription
            {
                UserId = "other", EndpointHash = "other", Endpoint = "https://web.push.apple.com/other"
            });
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/StudyReminders/Unsubscribe",
            new { endpoint = "https://web.push.apple.com/other" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/StudyReminders/DisableAll", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = app.Services.CreateScope();
        var remaining = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().StudyPushSubscriptions.ToListAsync();
        remaining.Should().ContainSingle().Which.UserId.Should().Be("other");
    }

    [Fact]
    public async Task InvalidInputAndAnotherAccountsSubscriptionAreRejected()
    {
        await using var app = EnabledApp();
        using var client = await ClientAsync(app);
        var input = Input();
        foreach (var invalid in new[]
        {
            input with { endpoint = "https://127.0.0.1/internal" },
            input with { timeZoneId = "Mars/Nowhere" },
            input with { auth = "bad" },
            input with { minuteOfDay = 1440 }
        })
        {
            (await client.PostAsJsonAsync("/StudyReminders/Subscribe", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        (await client.PostAsJsonAsync("/StudyReminders/Subscribe", input)).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.StudyPushSubscriptions.SingleAsync()).UserId = "someone-else";
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/StudyReminders/Subscribe", input)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var response = await client.PostAsJsonAsync("/StudyReminders/Status", new { input.endpoint });
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("enabled").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public async Task DisabledConfigurationFailsExplicitlyButStillAllowsOptOut()
    {
        await using var app = new SignedInWebApplicationFactory();
        using var client = await ClientAsync(app);
        (await client.PostAsJsonAsync("/StudyReminders/Subscribe", Input())).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.PostAsJsonAsync("/StudyReminders/DisableAll", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NotificationsPageShowsLocalizedPushSettingsInAllCultures(bool confirmed)
    {
        await using var app = new SignedInWebApplicationFactory();
        // The base fake identity is not an ApplicationUser; replace it for the Identity settings page.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Remove((await db.Users.FindAsync(SignedInWebApplicationFactory.UserId))!);
            await db.SaveChangesAsync();
            db.Add(new ApplicationUser
            {
                Id = SignedInWebApplicationFactory.UserId, FirstName = "Test", LastName = "Student",
                UserName = SignedInWebApplicationFactory.UserName, EmailConfirmed = confirmed
            });
            await db.SaveChangesAsync();
        }
        using var client = app.CreateClient();
        foreach (var culture in new[] { "en-US", "pt-BR", "fr-CA" })
        {
            var response = await client.GetAsync($"/Identity/Account/Manage/Notifications?culture={culture}&ui-culture={culture}");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            html.Should().Contain("id=\"study-reminders\"").And.Contain("id=\"study-reminder-time\"")
                .And.Contain("id=\"disable-all-study-reminders\"").And.NotContain("Push.");
        }
    }

    private static WebApplicationFactory<Program> EnabledApp()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        var settings = new Dictionary<string, string?>
        {
            ["WebPush:Enabled"] = "true", ["WebPush:Subject"] = "mailto:push@example.test",
            ["WebPush:PublicKey"] = WebEncoders.Base64UrlEncode([4, .. p.Q.X!, .. p.Q.Y!]),
            ["WebPush:PrivateKey"] = WebEncoders.Base64UrlEncode(p.D!)
        };
        return new SignedInWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));
    }

    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var html = await client.GetStringAsync("/");
        var token = Regex.Match(html, "name=\"request-verification-token\" content=\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeEmpty();
        client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token));
        return client;
    }

    private sealed record SubscriptionInput(string endpoint, string p256dh, string auth, string timeZoneId, int minuteOfDay);
    private static SubscriptionInput Input()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        return new("https://web.push.apple.com/test-token", WebEncoders.Base64UrlEncode([4, .. p.Q.X!, .. p.Q.Y!]),
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)), "America/Sao_Paulo", 1140);
    }
}
