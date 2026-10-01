using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// SolfegeMelody has no audio round: RequestPlay sends the melody itself so the page can
/// draw it on a staff, and keeps the expected answer for one ValidateExercise call. The
/// melody must reach the browser as plain JSON ({ melody: [{ type, note, duration }] }).
/// </summary>
public class SolfegeMelodyRoundTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;

    public SolfegeMelodyRoundTests(SignedInWebApplicationFactory factory) => _factory = factory;

    private int SeedExercise()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exercise = new Exercise { Name = "SolfegeMelody", Description = "Sing the melody" };
        db.Exercises.Add(exercise);
        db.SaveChanges();
        return exercise.ExerciseId;
    }

    // The layout's language form renders the antiforgery token; the page scripts send it
    // back in this header on every same-origin POST.
    private static async Task<HttpClient> WithAntiforgeryHeaderAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Home/Privacy");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the layout renders the language form");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", match.Groups[1].Value);
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    [Fact]
    public async Task RequestPlay_SendsTheMelodyAsPlainJson_AndValidateScoresItOnce()
    {
        var exerciseId = SeedExercise();
        var client = await WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

        play.EnumerateObject().Select(p => p.Name).Should().Equal("melody");
        var items = play.GetProperty("melody").EnumerateArray().ToList();
        items.Should().NotBeEmpty().And.AllSatisfy(item =>
        {
            item.EnumerateObject().Select(p => p.Name).Should().Equal("type", "note", "duration");
            item.GetProperty("type").GetString().Should().BeOneOf("note", "rest");
            item.GetProperty("note").GetString().Should().NotBeNullOrEmpty();
            item.GetProperty("duration").GetDouble().Should().BePositive();
        });
        var notes = items.Where(i => i.GetProperty("type").GetString() == "note")
            .Select(i => i.GetProperty("note").GetString()!)
            .ToList();
        notes.Should().NotBeEmpty("a melody starts with a note");

        var guess = new { exerciseId, userGuess = string.Join("|", notes), timeSpentSeconds = 3 };
        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", guess));
        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();

        var replay = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", guess));
        replay.GetProperty("success").GetBoolean().Should().BeFalse("the expected answer is used only once");
    }
}
