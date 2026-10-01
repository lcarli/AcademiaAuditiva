using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The learning path page and its dashboard card. The player completed step 1
/// (Higher or Lower) and has one right answer out of two on step 2 (Guess Interval).
/// </summary>
public class LearningPathPagesTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;

    public LearningPathPagesTests(SignedInWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Page_ShowsEveryUnitAndStep_WithTheCurrentGoal()
    {
        EnsureSeeded();

        var html = await GetPageAsync("/LearningPath");

        html.Should().Contain("<h1 class=\"display-6 mt-2 mb-1 text-body-emphasis\">Learning path</h1>")
            .And.Contain("1 of 18 steps")
            .And.Contain("First steps").And.Contain("Building blocks").And.Contain("Musicianship")
            .And.Contain("1 of 4 steps", "the first unit's count")
            .And.Contain("Goal: 7 right answers out of your last 10.")
            .And.Contain("1 of 7 right answers")
            .And.Contain("Complete the previous step to unlock it.");
        Regex.Count(html, "class=\"aa-path-step is-completed\"").Should().Be(1);
        Regex.Count(html, "class=\"aa-path-step is-current\" aria-current=\"step\"").Should().Be(1);
        Regex.Count(html, "class=\"aa-path-step is-locked\"").Should().Be(16);

        // Unlocked steps link to their exercise with the preset; locked ones do not link.
        html.Should().Contain("<a href=\"/Exercise/HigherOrLower\">Higher or Lower</a>")
            .And.Contain("<a href=\"/Exercise/GuessInterval?keySelect=C4&scaleTypeSelect=major\">Guess Interval</a>")
            .And.NotContain("href=\"/Exercise/GuessChords");
        html.Should().Contain("Key: C").And.Contain("Continue");
        html.Should().NotMatchRegex(@"LearningPath\.\w", "every text has a resource");
    }

    [Fact]
    public async Task Dashboard_ShowsTheCurrentStep()
    {
        EnsureSeeded();

        var html = await GetPageAsync("/Dashboard");

        var card = Regex.Match(html, "<section class=\"aa-card aa-path-card.*?</section>", RegexOptions.Singleline);
        card.Success.Should().BeTrue("the dashboard shows the learning path card");
        card.Value.Should().Contain("Learning path")
            .And.Contain("Guess Interval")
            .And.Contain("Step 2 of 18")
            .And.Contain("Unit 1: First steps")
            .And.Contain("1 of 7 right answers")
            .And.Contain("href=\"/Exercise/GuessInterval?keySelect=C4&scaleTypeSelect=major\"")
            .And.Contain("href=\"/LearningPath\"")
            .And.Contain("1 of 18 steps");
    }

    [Fact]
    public async Task Page_IsLocalized()
    {
        EnsureSeeded();

        var html = await GetPageAsync("/LearningPath?culture=fr-CA");

        html.Should().Contain("Parcours d’apprentissage")
            .And.Contain("1 sur 18 étapes")
            .And.Contain("Premiers pas");
        html.Should().NotMatchRegex(@"LearningPath\.\w");
    }

    // Each class gets its own factory, so its database only holds this player.
    private void EnsureSeeded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Exercises.Any()) return;

        SeedData.SeedExercises(db);
        var ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);
        var answers = Enumerable.Repeat(("HigherOrLower", true), 8).Append(("GuessInterval", true)).Append(("GuessInterval", false));
        var at = DateTime.UtcNow.AddHours(-1);
        foreach (var (exercise, correct) in answers)
        {
            at = at.AddSeconds(10);
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId,
                ExerciseId = ids[exercise],
                IsCorrect = correct,
                TimeSpentSeconds = 5,
                Timestamp = at,
            });
        }
        db.SaveChanges();
    }

    /// <summary>GETs a page with the time zone cookie the layout sets; returns its decoded HTML.</summary>
    private async Task<string> GetPageAsync(string url)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{UserTimeZone.CookieName}={Uri.EscapeDataString("America/Toronto")}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
