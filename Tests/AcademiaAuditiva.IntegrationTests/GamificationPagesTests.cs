using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The dashboard progress row and the badges page. The player has answered three
/// questions today, which earns the first_session badge when a page loads.
/// </summary>
public class GamificationPagesTests : IClassFixture<SignedInWebApplicationFactory>
{
    private const string Toronto = "America/Toronto";
    private readonly SignedInWebApplicationFactory _factory;

    public GamificationPagesTests(SignedInWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_ShowsLevelStreakAndBadges()
    {
        EnsureSeeded();

        var html = await GetPageAsync("/Dashboard");

        html.Should().Contain("Level 1")
            .And.Contain("80 XP", "3 correct answers and one badge")
            .And.Contain("1 day")
            .And.Contain("You practiced today. See you tomorrow!")
            .And.Contain("1 of 18")
            .And.Contain("href=\"/Dashboard/Achievements\"");
        html.Should().MatchRegex(@"<img src=""/img/badges/first_session\.webp\?v=[\w-]+""", "the latest badges show their art");
        html.Should().NotContain("Gamification.", "every text has a resource");
        html.Should().NotMatchRegex(@"Badge\.\w+\.(Title|Description)");
    }

    [Fact]
    public async Task Achievements_ListsEveryBadge_HighlightsNewOnesOnce_AndIsLocalized()
    {
        EnsureSeeded();

        var html = await GetPageAsync("/Dashboard/Achievements");

        Regex.Count(html, "<article class=\"aa-medal ").Should().Be(BadgeCatalog.Available.Count);
        foreach (var badge in BadgeCatalog.Available)
        {
            html.Should().MatchRegex($@"<img src=""/img/badges/{Regex.Escape(badge.Key)}\.webp\?v=[\w-]+""", "every medal shows its art");
        }
        ShouldHaveGroupHeadings(html, ("dedication", "Dedication"), ("mastery", "Mastery"), ("progress", "Progress"), ("fun", "Fun"));
        var earnedOn = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, Toronto)
            .ToString("d", CultureInfo.GetCultureInfo("en-US"));
        html.Should().Contain("First notes").And.Contain($"Earned on {earnedOn}");
        Regex.Count(html, "Not earned yet").Should().Be(BadgeCatalog.Available.Count - 1);
        html.Should().Contain("is-new", "the badge was not seen yet");

        var again = await GetPageAsync("/Dashboard/Achievements");

        again.Should().NotContain("is-new", "the first visit marked the badge as seen");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.BadgesEarned.SingleAsync(b => b.UserId == SignedInWebApplicationFactory.UserId))
                .IsNew.Should().BeFalse();
        }

        var french = await GetPageAsync("/Dashboard/Achievements?culture=fr-CA");

        french.Should().Contain("Premières notes").And.Contain("Pas encore obtenu");
        ShouldHaveGroupHeadings(french, ("dedication", "Assiduité"), ("mastery", "Maîtrise"), ("progress", "Progression"), ("fun", "Plaisir"));
    }

    private static void ShouldHaveGroupHeadings(string html, params (string Id, string Name)[] groups)
    {
        foreach (var (id, name) in groups)
        {
            html.Should().MatchRegex($"id=\"badges-{id}\"[^>]*>\\s*{Regex.Escape(name)}\\s*<span");
        }
    }

    // Each class gets its own factory, so its database only holds this player.
    private void EnsureSeeded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Badges.Any()) return;

        db.Badges.AddRange(SeedData.BadgeSeed());
        var exercise = new Exercise { Name = "GuessNote", Description = "Guess the note" };
        db.Exercises.Add(exercise);
        db.SaveChanges();
        for (var i = 0; i < 3; i++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId,
                ExerciseId = exercise.ExerciseId,
                IsCorrect = true,
                TimeSpentSeconds = 5,
                Timestamp = DateTime.UtcNow,
            });
        }
        db.SaveChanges();
    }

    /// <summary>GETs a page with the time zone cookie the layout sets; returns its decoded HTML.</summary>
    private async Task<string> GetPageAsync(string url)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{UserTimeZone.CookieName}={Uri.EscapeDataString(Toronto)}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Razor encodes non-ASCII text (Premi&#xE8;res).
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
