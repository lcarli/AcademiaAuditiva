using System.Net.Http.Json;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// ValidateExercise returns the XP, level and badges each answer earned, with every
/// text already localized for wwwroot/js/core/rewards.js. SolfegeMelody is used
/// because RequestPlay sends its melody, the right answer, itself (its starting note
/// goes to the recording mixer).
/// </summary>
public class GamificationRewardsTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public GamificationRewardsTests(ExploreWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Answers_EarnXpBadgesAndLevels_WithLocalizedTexts()
    {
        var exerciseId = Seed();
        var client = await WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

        // First answer: 10 XP plus 50 for the first_session badge.
        var rewards = await AnswerAsync(client, exerciseId, correct: true);
        rewards.GetProperty("xp").GetInt32().Should().Be(60);
        rewards.GetProperty("xpGained").GetInt32().Should().Be(60);
        rewards.GetProperty("xpGainedText").GetString().Should().Be("+60 XP");
        rewards.GetProperty("level").GetInt32().Should().Be(1);
        rewards.GetProperty("levelText").GetString().Should().Be("Level 1");
        rewards.GetProperty("levelPercent").GetInt32().Should().Be(60);
        rewards.GetProperty("levelUp").GetBoolean().Should().BeFalse();
        rewards.GetProperty("rank").GetProperty("symbol").GetString().Should().Be("pp");
        rewards.GetProperty("rank").GetProperty("name").GetString().Should().Be("Pianissimo");
        var badge = rewards.GetProperty("badges").EnumerateArray().Should().ContainSingle().Subject;
        badge.GetProperty("key").GetString().Should().Be("first_session");
        badge.GetProperty("title").GetString().Should().Be("First notes");
        badge.GetProperty("description").GetString().Should().Be("Answer your first exercise.");
        badge.GetProperty("image").GetString().Should().MatchRegex(@"^/img/badges/first_session\.webp\?v=[\w-]+$");
        badge.GetProperty("group").GetString().Should().Be("dedication");
        var celebration = rewards.GetProperty("celebration");
        celebration.GetProperty("title").GetString().Should().Be("New badge!");
        celebration.GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);
        celebration.GetProperty("badgesHeading").ValueKind.Should().Be(JsonValueKind.Null);
        celebration.GetProperty("viewAllText").GetString().Should().Be("View all badges");
        celebration.GetProperty("viewAllUrl").GetString().Should().Be("/Dashboard/Achievements");
        celebration.GetProperty("closeText").GetString().Should().Be("Keep practicing");

        // A wrong answer still earns a little XP, and nothing to celebrate.
        rewards = await AnswerAsync(client, exerciseId, correct: false);
        rewards.GetProperty("xpGained").GetInt32().Should().Be(2);
        rewards.GetProperty("xp").GetInt32().Should().Be(62);
        rewards.GetProperty("badges").GetArrayLength().Should().Be(0);
        rewards.GetProperty("celebration").ValueKind.Should().Be(JsonValueKind.Null);

        foreach (var xp in new[] { 72, 82, 92 })
        {
            rewards = await AnswerAsync(client, exerciseId, correct: true);
            rewards.GetProperty("xp").GetInt32().Should().Be(xp);
            rewards.GetProperty("levelUp").GetBoolean().Should().BeFalse();
            rewards.GetProperty("celebration").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 102 XP crosses the 100 XP needed for level 2.
        rewards = await AnswerAsync(client, exerciseId, correct: true);
        rewards.GetProperty("xp").GetInt32().Should().Be(102);
        rewards.GetProperty("level").GetInt32().Should().Be(2);
        rewards.GetProperty("levelText").GetString().Should().Be("Level 2");
        rewards.GetProperty("levelPercent").GetInt32().Should().Be(1);
        rewards.GetProperty("levelUp").GetBoolean().Should().BeTrue();
        rewards.GetProperty("badges").GetArrayLength().Should().Be(0);
        celebration = rewards.GetProperty("celebration");
        celebration.GetProperty("title").GetString().Should().Be("Level up!");
        celebration.GetProperty("text").GetString().Should().Be("You are now level 2 (Pianissimo).");
        celebration.GetProperty("badgesHeading").ValueKind.Should().Be(JsonValueKind.Null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var earned = await db.BadgesEarned.Where(b => b.UserId == SignedInWebApplicationFactory.UserId).ToListAsync();
        earned.Should().ContainSingle().Which.Should().BeEquivalentTo(new { BadgeKey = "first_session", IsNew = true });
    }

    private int Seed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Badges.AddRange(SeedData.BadgeSeed());
        var exercise = new Exercise { Name = "SolfegeMelody", Description = "Sing the melody" };
        db.Exercises.Add(exercise);
        db.SaveChanges();
        return exercise.ExerciseId;
    }

    /// <summary>Plays one round and answers it; returns the rewards of the answer.</summary>
    private static async Task<JsonElement> AnswerAsync(HttpClient client, int exerciseId, bool correct)
    {
        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));
        var notes = play.GetProperty("melody").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "note")
            .Select(item => item.GetProperty("note").GetString());
        var guess = new { exerciseId, userGuess = correct ? string.Join("|", notes) : "X" };

        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", guess));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().Be(correct);
        validation.GetProperty("path").ValueKind.Should().Be(JsonValueKind.Null, "SolfegeMelody is not on the learning path");
        return validation.GetProperty("rewards");
    }

    private static Task<HttpClient> WithAntiforgeryHeaderAsync(HttpClient client)
        => IntegrationHttp.WithAntiforgeryHeaderAsync(client);

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => IntegrationHttp.ReadJsonAsync(response);
}
