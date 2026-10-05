using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The daily challenge card on the dashboard. The challenge depends on the date, so the tests draw
/// today's with <see cref="DailyChallengeRules.Pick"/>. The player completed the first exercise,
/// answered the second once and the third only two days ago.
/// </summary>
public class DailyChallengePagesTests : IClassFixture<SignedInWebApplicationFactory>
{
    private const string TimeZoneId = "America/Toronto";
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    private readonly SignedInWebApplicationFactory _factory;

    public DailyChallengePagesTests(SignedInWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_ShowsTodaysChallenge_UntilItIsComplete()
    {
        var picked = EnsureSeeded();
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var localizer = _factory.Services.GetRequiredService<IStringLocalizer<SharedResources>>();
        var targets = picked.Select(e => DailyChallengeRules.Target(e.Name)).ToList();

        var card = await GetCardAsync("/Dashboard");

        card.Should().Contain("Daily challenge").And.Contain("Complete today's exercises")
            .And.NotContain("is-complete");
        foreach (var exercise in picked)
        {
            card.Should().Contain($"<a class=\"aa-challenge-title stretched-link\" href=\"/Exercise/{exercise.Name}\">{localizer[exercise.Name].Value}</a>")
                .And.Contain($"<span class=\"aa-challenge-category\">{localizer[$"ExerciseCategory.{exercise.Category}"].Value}</span>");
        }
        card.Should().Contain($"{targets[0]} of {targets[0]} answers")
            .And.Contain($"1 of {targets[1]} answers")
            .And.Contain($"0 of {targets[2]} answers", "an answer from another day doesn't count");
        Regex.Count(card, "class=\"aa-challenge-item is-done\"").Should().Be(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AddAnswers(db, picked[1], targets[1] - 1, DateTime.UtcNow);
            AddAnswers(db, picked[2], targets[2], DateTime.UtcNow);
            db.SaveChanges();
        }
        card = await GetCardAsync("/Dashboard");

        card.Should().Contain("aa-challenge-card p-4 mb-4 is-complete")
            .And.Contain("Challenge complete!")
            .And.Contain("Come back tomorrow");
        Regex.Count(card, "class=\"aa-challenge-item is-done\"").Should().Be(3);
    }

    [Fact]
    public async Task Card_IsLocalized()
    {
        var picked = EnsureSeeded();

        var card = await GetCardAsync("/Dashboard?culture=pt-BR");

        card.Should().Contain("Desafio do dia").And.MatchRegex(@"\d de \d respostas");
        card.Should().NotMatchRegex(@"DailyChallenge\.\w").And.NotMatchRegex(@"ExerciseCategory\.\w");
        foreach (var exercise in picked)
        {
            card.Should().NotContain($">{exercise.Name}</a>", "exercise names are translated");
        }
    }

    /// <summary>Seeds the exercises and the player's answers once per class; returns today's challenge.</summary>
    private IReadOnlyList<ChallengeExercise> EnsureSeeded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seeded = db.Exercises.Any();
        if (!seeded) SeedData.SeedExercises(db);

        var exercises = db.Exercises
            .Select(e => new ChallengeExercise(e.ExerciseId, e.Name, e.ExerciseCategory.Name))
            .ToList();
        var picked = DailyChallengeRules.Pick(PracticeStreak.LocalDate(DateTime.UtcNow, Toronto), exercises);
        picked.Should().HaveCount(3);

        if (!seeded)
        {
            var now = DateTime.UtcNow;
            AddAnswers(db, picked[0], DailyChallengeRules.Target(picked[0].Name), now);
            AddAnswers(db, picked[1], 1, now);
            AddAnswers(db, picked[2], 1, now.AddDays(-2));
            db.SaveChanges();
        }
        return picked;
    }

    private static void AddAnswers(ApplicationDbContext db, ChallengeExercise exercise, int count, DateTime at)
    {
        for (var i = 0; i < count; i++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId,
                ExerciseId = exercise.ExerciseId,
                IsCorrect = i % 2 == 0,
                TimeSpentSeconds = 5,
                Timestamp = at,
            });
        }
    }

    /// <summary>GETs the dashboard in the player's time zone; returns the challenge card's decoded HTML.</summary>
    private async Task<string> GetCardAsync(string url)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{UserTimeZone.CookieName}={Uri.EscapeDataString(TimeZoneId)}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var card = Regex.Match(html, "<section class=\"aa-card aa-challenge-card.*?</section>", RegexOptions.Singleline);
        card.Success.Should().BeTrue("the dashboard shows the daily challenge card");
        return card.Value;
    }
}
