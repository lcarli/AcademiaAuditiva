using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Which note changed? (GuessChangedNote) offers a button for each note of the melody and two
/// for where the note went, with its texts in the student's language. Its round plays the two
/// melodies without telling the answer, only how many notes they have.
/// </summary>
public class GuessChangedNoteTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public GuessChangedNoteTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("en-US", "Which Note Changed?", "Which note changed, and did it go up or down?", "Up|Down",
        "note {0}, which went up", "note {0}, which went down", "Count the notes as you listen: only one of them changes.")]
    [InlineData("pt-BR", "Qual nota mudou?", "Qual nota mudou, e ela subiu ou desceu?", "Subiu|Desceu",
        "a nota {0}, que subiu", "a nota {0}, que desceu", "Conte as notas enquanto ouve: só uma delas muda.")]
    [InlineData("fr-CA", "Quelle note a changé\u00A0?", "Quelle note a changé, et est-elle montée ou descendue\u00A0?", "Montée|Descendue",
        "la note {0}, qui est montée", "la note {0}, qui est descendue", "Comptez les notes en écoutant\u00A0: une seule d’entre elles change.")]
    public async Task Page_OffersEveryNoteAndWhereItWent_InTheStudentsLanguage(
        string culture, string title, string question, string directions, string answerUp, string answerDown, string tip)
    {
        var html = await _factory.CreateClient().GetStringAsync($"/Exercise/GuessChangedNote?culture={culture}");

        Regex.Matches(html, "<button type=\"button\" class=\"aa-answer guessPosition\" value=\"(\\w+)\">([^<]*)</button>")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .Should().Equal(Enumerable.Range(1, 8).Select(n => (n.ToString(), n.ToString())));
        Regex.Matches(html, "<button type=\"button\" class=\"aa-answer guessDirection\" value=\"(\\w+)\">([^<]*)</button>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(new[] { "up", "down" }.Zip(directions.Split('|')));
        // The page puts the note's number in the answer it shows.
        Attribute(html, "data-answer-up").Should().Be(answerUp);
        Attribute(html, "data-answer-down").Should().Be(answerDown);

        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain(title).And.Contain(question).And.Contain(tip);
        text.Should().NotMatchRegex(@"Exercise\.(GuessChangedNote|SelectGuessChangedNote|ChangedNote\.)", "every text has a resource");
    }

    [Fact]
    public async Task Round_PlaysTheTwoMelodies_AndTellsOnlyHowManyNotesTheyHave()
    {
        var (exerciseId, client) = await StartAsync();

        var play = await PlayAsync(client, exerciseId, melodyLength: 6);

        play.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["roundId", "melody1Token", "melody2Token", "notes"], "the round doesn't tell the melodies nor the answer");
        play.GetProperty("melody1Token").GetString().Should().NotBeNullOrEmpty();
        play.GetProperty("melody2Token").GetString().Should().NotBeNullOrEmpty();
        play.GetProperty("notes").GetInt32().Should().Be(6, "the page offers a button for each note");
        _factory.Mixer.Plans.Should().HaveCount(2).And.AllSatisfy(melody => melody.Should().HaveCount(6));
    }

    [Fact]
    public async Task Round_IsRight_OnlyWithTheNoteThatChanged_AndWhereItWent()
    {
        var (exerciseId, client) = await StartAsync();

        var play = await PlayAsync(client, exerciseId, melodyLength: 5);
        var (position, up) = ChangedNote();
        var right = await ValidateAsync(client, exerciseId, play, $"{position}|{(up ? "up" : "down")}");

        right.GetProperty("success").GetBoolean().Should().BeTrue();
        right.GetProperty("isCorrect").GetBoolean().Should().BeTrue();

        _factory.Mixer.Plans.Clear();
        play = await PlayAsync(client, exerciseId, melodyLength: 5);
        (position, up) = ChangedNote();
        var wrongWay = await ValidateAsync(client, exerciseId, play, $"{position}|{(up ? "down" : "up")}");

        wrongWay.GetProperty("success").GetBoolean().Should().BeTrue();
        wrongWay.GetProperty("isCorrect").GetBoolean().Should().BeFalse("the note went the other way");
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "GuessChangedNote").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    private static async Task<JsonElement> PlayAsync(HttpClient client, int exerciseId, int melodyLength)
    {
        var filters = new Dictionary<string, string> { ["melodyLength"] = melodyLength.ToString() };
        return await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, filters }));
    }

    // The note that sounds different in the second melody, from 1, and whether it is higher.
    private (int Position, bool Up) ChangedNote()
    {
        var melodies = _factory.Mixer.Plans.ToList();
        melodies.Should().HaveCount(2);
        var changed = melodies[0].Zip(melodies[1]).Select((pair, i) => (Was: pair.First, Now: pair.Second, Position: i + 1))
            .Where(note => note.Was != note.Now).ToList();
        var (was, now, position) = changed.Should().ContainSingle("one note changes").Subject;
        return (position, Midi(now) > Midi(was));
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, int exerciseId, JsonElement play, string userGuess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));

    private static string Attribute(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, $"{name}=\"([^\"]*)\"").Groups[1].Value);

    private static int Midi(MixInput note) =>
        MusicTheoryService.NoteToMidi(note.SampleName[..^".mp3".Length].Replace('s', '#')) ?? throw new ArgumentException(note.SampleName);
}
