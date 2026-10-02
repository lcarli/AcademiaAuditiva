using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The instrument picked in the exercise filters is kept in the <c>instrument</c> cookie:
/// the exercise page offers its octaves, and every round is mixed from its samples. The
/// exercises that play chords leave out the violin, which plays one note at a time.
/// </summary>
public class InstrumentPlaybackTests : IClassFixture<ExploreWebApplicationFactory>
{
    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly ExploreWebApplicationFactory _factory;

    public InstrumentPlaybackTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
    }

    [Theory]
    [InlineData("Guitar", "C1-C6", @"^guitar/[A-G]s?[2-5]\.mp3$")]
    [InlineData("violin", "C1-C2", @"^violin/[A-G]s?4\.mp3$")]
    [InlineData("Piano", "C1-C1", @"^[A-G]s?1\.mp3$")]
    [InlineData(null, null, @"^[A-G]s?4\.mp3$")]
    [InlineData("Drums", "C2-C2", @"^[A-G]s?2\.mp3$")]
    public async Task Round_IsMixedFromTheSamplesOfTheInstrumentInTheCookie(string? instrument, string? noteRange, string sample)
    {
        var exerciseId = ExerciseId("GuessNote");
        var client = await ClientAsync(("instrument", instrument), ("noteRange", noteRange));

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

        play.GetProperty("playToken").GetString().Should().NotBeNullOrEmpty();
        _factory.Mixer.Plans.Should().ContainSingle().Which.Should().ContainSingle()
            .Which.SampleName.Should().MatchRegex(sample);
    }

    [Fact]
    public async Task ExercisePage_OffersTheOctavesOfTheInstrumentInTheCookie()
    {
        ExerciseId("GuessNote");
        var client = await ClientAsync(("instrument", "Violin"));

        var html = await client.GetStringAsync("/Exercise/GuessNote");

        Buttons(html).Should().Equal(
            new Button("Piano", "false", "1", "6", "Piano"),
            new Button("Guitar", "false", "2", "5", "Guitar"),
            new Button("Violin", "true", "4", "6", "Violin"));
        html.Should().Contain("id=\"rangeStart\" min=\"4\" max=\"6\" value=\"4\"")
            .And.Contain("id=\"rangeEnd\" min=\"4\" max=\"6\" value=\"4\"");
    }

    [Fact]
    public async Task ExercisePage_OffersThePiano_WithoutACookie()
    {
        ExerciseId("GuessNote");
        var client = await ClientAsync();

        var html = await client.GetStringAsync("/Exercise/GuessNote");

        Buttons(html).Where(button => button.Pressed == "true").Should().ContainSingle()
            .Which.Should().Be(new Button("Piano", "true", "1", "6", "Piano"));
        html.Should().Contain("id=\"rangeStart\" min=\"1\" max=\"6\" value=\"4\"");
    }

    [Fact]
    public async Task ChordExercisePage_LeavesOutTheViolin()
    {
        ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Violin"));

        var html = await client.GetStringAsync("/Exercise/GuessChords");

        Buttons(html).Should().Equal(
            new Button("Piano", "true", "1", "6", "Piano"),
            new Button("Guitar", "false", "2", "5", "Guitar"));
        html.Should().Contain("id=\"rangeStart\" min=\"1\" max=\"6\" value=\"4\"");
    }

    [Fact]
    public async Task ChordRound_OnTheViolin_IsPlayedOnThePiano()
    {
        var exerciseId = ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Violin"));

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

        play.GetProperty("playToken").GetString().Should().NotBeNullOrEmpty();
        _factory.Mixer.Plans.Should().ContainSingle().Which.Should().NotBeEmpty()
            .And.AllSatisfy(input => input.SampleName.Should().MatchRegex(@"^[A-G]s?\d\.mp3$"));
    }

    [Fact]
    public async Task ChordRound_OnTheGuitar_IsStrummed()
    {
        var exerciseId = ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Guitar"));

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

        play.GetProperty("playToken").GetString().Should().NotBeNullOrEmpty();
        var strum = _factory.Mixer.Plans.Should().ContainSingle().Subject;
        strum.Should().HaveCountGreaterThanOrEqualTo(4)
            .And.AllSatisfy(input => input.SampleName.Should().MatchRegex(@"^guitar/[A-G]s?\d\.mp3$"));
        strum.Select(input => input.StartTimeSeconds).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    private sealed record Button(string Instrument, string Pressed, string Lowest, string Highest, string Text);

    private static List<Button> Buttons(string html) =>
        [
            .. Regex.Matches(html,
                    "<button type=\"button\"\\s+data-instrument=\"(\\w+)\"\\s+data-lowest-octave=\"(\\d+)\"\\s+"
                    + "data-highest-octave=\"(\\d+)\"\\s+aria-pressed=\"(\\w+)\">([^<]*)</button>")
                .Select(m => new Button(m.Groups[1].Value, m.Groups[4].Value,
                    m.Groups[2].Value, m.Groups[3].Value, m.Groups[5].Value)),
        ];

    private int ExerciseId(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
        return db.Exercises.Single(e => e.Name == name).ExerciseId;
    }

    private Task<HttpClient> ClientAsync(params (string Name, string? Value)[] cookies)
    {
        var container = new CookieContainer();
        foreach (var (name, value) in cookies)
        {
            if (value is not null) container.Add(BaseAddress, new Cookie(name, value));
        }
        return IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(container)));
    }
}
