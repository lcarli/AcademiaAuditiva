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
/// The singing exercises (SingNote, SingInterval, SingMelody) record the student in the
/// browser, which sends the notes it heard. Their rounds play what to sing without telling
/// its notes: SingInterval tells only the interval and its direction, and plays the first note.
/// </summary>
public class SingingTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public SingingTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("SingNote", "en-US", "Sing the Note", "Hum the note quietly before you record, then hold it steady for a second or two.")]
    [InlineData("SingNote", "pt-BR", "Cante a nota", "Cantarole a nota baixinho antes de gravar e depois sustente-a firme por um ou dois segundos.")]
    [InlineData("SingNote", "fr-CA", "Chantez la note", "Fredonnez la note doucement avant d’enregistrer, puis tenez-la bien stable une seconde ou deux.")]
    [InlineData("SingInterval", "en-US", "Sing the Interval", "Take a short breath between the two notes, and hold each one for a second.")]
    [InlineData("SingInterval", "pt-BR", "Cante o intervalo", "Respire rapidamente entre as duas notas e sustente cada uma por um segundo.")]
    [InlineData("SingInterval", "fr-CA", "Chantez l’intervalle", "Respirez brièvement entre les deux notes et tenez chacune une seconde.")]
    [InlineData("SingMelody", "en-US", "Sing the Melody", "From four notes on, one wrong, missing or extra note is forgiven.")]
    [InlineData("SingMelody", "pt-BR", "Cante a melodia", "A partir de quatro notas, uma nota errada, faltando ou a mais é perdoada.")]
    [InlineData("SingMelody", "fr-CA", "Chantez la mélodie", "À partir de quatre notes, une note fausse, manquante ou en trop est pardonnée.")]
    public async Task Page_RecordsTheStudent_AndAnalyzesTheRecordingInTheBrowser_InTheirLanguage(
        string exercise, string culture, string title, string tip)
    {
        var html = await _factory.CreateClient().GetStringAsync($"/Exercise/{exercise}?culture={culture}");

        html.Should().Contain("id=\"recordAudio\"").And.Contain("id=\"micMeter\"")
            .And.Contain("/js/core/pitch-detector.js").And.Contain($"/js/Exercises/{exercise}.js");
        html.Should().NotContain("essentia", "the pitch detector is our own");
        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain(title).And.Contain(tip);
        text.Should().NotMatchRegex(@"Exercise\.(Sing|Interval\.|Direction\.)", "every text has a resource");
    }

    [Theory]
    [InlineData("en-US", "Perfect 5th", "Ascending", "Descending")]
    [InlineData("pt-BR", "5ª justa", "Ascendente", "Descendente")]
    [InlineData("fr-CA", "5te juste", "Ascendante", "Descendante")]
    public async Task SingIntervalPage_NamesTheIntervals_AndTheirDirections_InTheStudentsLanguage(
        string culture, string fifth, string up, string down)
    {
        var html = await _factory.CreateClient().GetStringAsync($"/Exercise/SingInterval?culture={culture}");

        // SingInterval.js names the round's interval by its semitones, from the unison to the octave.
        var names = Regex.Matches(html, "<span data-semitones=\"(\\d+)\">([^<]*)</span>")
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => WebUtility.HtmlDecode(m.Groups[2].Value));
        names.Keys.Should().BeEquivalentTo(Enumerable.Range(0, 13));
        names[7].Should().Be(fifth);
        Attribute(html, "data-direction-asc").Should().Be(up);
        Attribute(html, "data-direction-desc").Should().Be(down);
    }

    [Theory]
    [InlineData("SingNote", new[] { "roundId", "playToken" }, 1)]
    [InlineData("SingInterval", new[] { "roundId", "playToken", "interval", "direction" }, 1)]
    [InlineData("SingMelody", new[] { "roundId", "playToken" }, 5)]
    public async Task Round_PlaysWhatToSing_WithoutTellingItsNotes(string exercise, string[] fields, int notesPlayed)
    {
        var (exerciseId, client) = await StartAsync(exercise);

        var play = await PlayAsync(client, exerciseId);

        play.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(fields);
        play.GetProperty("playToken").GetString().Should().NotBeNullOrEmpty();
        _factory.Mixer.Plans.Should().ContainSingle().Which.Should().HaveCount(notesPlayed,
            "SingInterval plays only the first note: the second is the student's to find");
    }

    [Fact]
    public async Task SingNote_IsRightInAnyOctave_ButNotOnAnotherNote()
    {
        var (exerciseId, client) = await StartAsync("SingNote");

        var play = await PlayAsync(client, exerciseId);
        var note = PlayedNotes().Single();
        var right = await ValidateAsync(client, exerciseId, play, MusicTheoryService.MidiToNote(note - 12));

        right.GetProperty("success").GetBoolean().Should().BeTrue();
        right.GetProperty("isCorrect").GetBoolean().Should().BeTrue("an octave down is the same note");

        _factory.Mixer.Plans.Clear();
        play = await PlayAsync(client, exerciseId);
        note = PlayedNotes().Single();
        var wrong = await ValidateAsync(client, exerciseId, play, MusicTheoryService.MidiToNote(note + 2));

        wrong.GetProperty("success").GetBoolean().Should().BeTrue();
        wrong.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("all")]
    public async Task SingInterval_IsRight_WithThePlayedNoteThenTheIntervalFromIt(string level)
    {
        var (exerciseId, client) = await StartAsync("SingInterval");
        var filters = new Dictionary<string, string> { ["siLevel"] = level, ["intervalDirection"] = "both" };

        for (var i = 0; i < 4; i++)
        {
            _factory.Mixer.Plans.Clear();
            var play = await PlayAsync(client, exerciseId, filters, free: true);
            var first = PlayedNotes().Single();
            var steps = MusicTheoryService.IntervalSemitones[play.GetProperty("interval").GetString()!];
            MusicTheoryService.SingIntervalLevels[level].Should().Contain(play.GetProperty("interval").GetString());
            var second = play.GetProperty("direction").GetString() == "asc" ? first + steps : first - steps;
            var expected = $"{MusicTheoryService.MidiToNote(first)}|{MusicTheoryService.MidiToNote(second)}";

            var reveal = await RevealAsync(client, exerciseId, play);
            reveal.GetProperty("answer").GetString().Should().Be(expected, "the answer is the note played and the interval from it");

            // Sung an octave down it is right; a semitone short, wrong.
            var sung = i % 2 == 0
                ? $"{MusicTheoryService.MidiToNote(first - 12)}|{MusicTheoryService.MidiToNote(second - 12)}"
                : $"{MusicTheoryService.MidiToNote(first)}|{MusicTheoryService.MidiToNote(second + (second > first ? -1 : 1))}";
            var validation = await ValidateAsync(client, exerciseId, play, sung);

            validation.GetProperty("success").GetBoolean().Should().BeTrue();
            validation.GetProperty("isCorrect").GetBoolean().Should().Be(i % 2 == 0, sung);
            validation.GetProperty("answer").GetString().Should().Be(expected);
        }
    }

    [Fact]
    public async Task SingMelody_IsRight_WithTheMelodySungInAnyOctave()
    {
        var (exerciseId, client) = await StartAsync("SingMelody");

        var play = await PlayAsync(client, exerciseId, new() { ["melodyLength"] = "6" }, free: true);
        var played = PlayedNotes();
        var answer = (await RevealAsync(client, exerciseId, play)).GetProperty("answer").GetString()!;

        played.Should().HaveCount(6);
        answer.Split('|').Select(note => MusicTheoryService.NoteToMidi(note)).Should().Equal(played.Select(note => (int?)note));
        var validation = await ValidateAsync(client, exerciseId, play,
            string.Join("|", played.Select(note => MusicTheoryService.MidiToNote(note + 12))));

        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        validation.GetProperty("answer").GetString().Should().Be(answer);
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync(string exercise)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == exercise).ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    private static async Task<JsonElement> PlayAsync(
        HttpClient client, int exerciseId, Dictionary<string, string>? filters = null, bool free = false) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay",
            new { exerciseId, filters = filters ?? new Dictionary<string, string>(), free }));

    private static async Task<JsonElement> RevealAsync(HttpClient client, int exerciseId, JsonElement play) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RevealAnswer",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString() }));

    private static async Task<JsonElement> ValidateAsync(HttpClient client, int exerciseId, JsonElement play, string userGuess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));

    // The MIDI notes of the round's only mix, on the piano: "Cs4.mp3" is C#4.
    private List<int> PlayedNotes() =>
        [.. _factory.Mixer.Plans.Should().ContainSingle().Subject.Select(input =>
            MusicTheoryService.NoteToMidi(input.SampleName[..^".mp3".Length].Replace('s', '#')) ?? throw new ArgumentException(input.SampleName))];

    private static string Attribute(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, $"{name}=\"([^\"]*)\"").Groups[1].Value);
}
