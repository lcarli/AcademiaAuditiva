using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The instrument picked in the exercise filters is kept in the <c>instrument</c> cookie:
/// the exercise page offers its octaves, and every round is mixed from its samples. The
/// exercises about chords leave out the violin, which plays one note at a time, and on the
/// guitar those that play chords offer where on the neck to play them (the
/// <c>guitarPosition</c> cookie) instead of the note range, which only the exercises whose
/// notes come from it offer.
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
    [InlineData("violin", "C1-C2", @"^violin/(G|Gs|A|As|B)3\.mp3$")]
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

        // The violin starts at its G string, the guitar at its low E string.
        Buttons(html).Should().Equal(
            new Button("Piano", "false", "1", "6", "C1", "false", "Piano"),
            new Button("Guitar", "false", "2", "5", "E2", "true", "Guitar"),
            new Button("Violin", "true", "3", "6", "G3", "false", "Violin"));
        html.Should().Contain("id=\"rangeStart\" min=\"3\" max=\"6\" value=\"4\"")
            .And.Contain("id=\"rangeEnd\" min=\"3\" max=\"6\" value=\"4\"")
            .And.Contain("id=\"rangeStartLabel\" class=\"fw-semibold\">C4<");
    }

    [Fact]
    public async Task ExercisePage_OffersThePiano_WithoutACookie()
    {
        ExerciseId("GuessNote");
        var client = await ClientAsync();

        var html = await client.GetStringAsync("/Exercise/GuessNote");

        Buttons(html).Where(button => button.Pressed == "true").Should().ContainSingle()
            .Which.Should().Be(new Button("Piano", "true", "1", "6", "C1", "false", "Piano"));
        html.Should().Contain("id=\"rangeStart\" min=\"1\" max=\"6\" value=\"4\"");
    }

    [Theory]
    [InlineData("GuessChords")]
    [InlineData("CompleteChord")]
    [InlineData("GuessTopNote")]
    public async Task ChordExercisePage_LeavesOutTheViolin(string exerciseName)
    {
        ExerciseId(exerciseName);
        var client = await ClientAsync(("instrument", "Violin"));

        var html = await client.GetStringAsync($"/Exercise/{exerciseName}");

        Buttons(html).Should().Equal(
            new Button("Piano", "true", "1", "6", "C1", "false", "Piano"),
            new Button("Guitar", "false", "2", "5", "E2", "true", "Guitar"));
    }

    [Fact]
    public async Task ChordExercisePage_WithTheViolinInTheCookie_OffersTheRangeOfThePiano()
    {
        ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Violin"));

        var html = await client.GetStringAsync("/Exercise/GuessChords");

        html.Should().Contain("<div class=\"mb-3\" id=\"rangeFilter\">")
            .And.Contain("id=\"rangeStart\" min=\"1\" max=\"6\" value=\"4\"");
    }

    [Theory]
    [InlineData(null, "Open")]
    [InlineData("Open", "Open")]
    [InlineData("barre", "Barre")]
    [InlineData("High", "High")]
    [InlineData("Drums", "Open")]
    public async Task ChordExercisePage_OnTheGuitar_OffersWhereOnTheNeckToPlay_InsteadOfTheRange(
        string? guitarPosition, string pressed)
    {
        ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Guitar"), ("guitarPosition", guitarPosition));

        var html = await client.GetStringAsync("/Exercise/GuessChords");

        Buttons(html).Where(button => button.Pressed == "true").Should().ContainSingle()
            .Which.Instrument.Should().Be("Guitar");
        html.Should().Contain("<div class=\"mb-3\" id=\"positionFilter\">")
            .And.Contain("<div class=\"mb-3\" id=\"rangeFilter\" hidden=\"hidden\">");
        Positions(html).Should().Equal(
            new Position("Open", pressed == "Open" ? "true" : "false", "Open"),
            new Position("Barre", pressed == "Barre" ? "true" : "false", "Barre"),
            new Position("High", pressed == "High" ? "true" : "false", "High on the neck"));
    }

    [Fact]
    public async Task ChordExercisePage_OnThePiano_OffersTheRange_AndKeepsThePositionForTheGuitar()
    {
        ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Piano"), ("guitarPosition", "High"));

        var html = await client.GetStringAsync("/Exercise/GuessChords");

        html.Should().Contain("<div class=\"mb-3\" id=\"positionFilter\" hidden=\"hidden\">")
            .And.Contain("<div class=\"mb-3\" id=\"rangeFilter\">");
        Positions(html).Where(position => position.Pressed == "true").Should().ContainSingle()
            .Which.Name.Should().Be("High", "picking the guitar shows the position picked before");
    }

    [Theory]
    [InlineData("HigherOrLower")]
    [InlineData("GuessNote")]
    [InlineData("GuessTopNote")]
    public async Task ExercisePage_ThatPlaysNoChordsOnTheNeck_OffersTheRangeOfTheGuitar(string exerciseName)
    {
        ExerciseId(exerciseName);
        var client = await ClientAsync(("instrument", "Guitar"), ("guitarPosition", "High"));

        var html = await client.GetStringAsync($"/Exercise/{exerciseName}");

        html.Should().NotContain("id=\"positionFilter\"")
            .And.Contain("<div class=\"mb-3\" id=\"rangeFilter\">")
            .And.Contain("id=\"rangeStart\" min=\"2\" max=\"5\" value=\"4\"");
    }

    // The exercises that set their octaves themselves, or with a filter of their own, would
    // ignore the range: they don't offer it.
    [Theory]
    [InlineData("GuessNote", true)]
    [InlineData("HigherOrLower", true)]
    [InlineData("GuessChords", true)]
    [InlineData("GuessQuality", true)]
    [InlineData("GuessFunction", true)]
    [InlineData("GuessDegree", true)]
    [InlineData("GuessInversion", true)]
    [InlineData("GuessCadence", true)]
    [InlineData("GuessProgression", true)]
    [InlineData("GuessTopNote", true)]
    [InlineData("GuessInterval", false)]
    [InlineData("GuessFullInterval", false)]
    [InlineData("IntervalMelodico", false)]
    [InlineData("GuessMissingNote", false)]
    [InlineData("GuessScaleType", false)]
    [InlineData("GuessGreekMode", false)]
    [InlineData("CompleteScale", false)]
    [InlineData("TransposeScale", false)]
    [InlineData("CompleteChord", false)]
    [InlineData("SolfegeMelody", false)]
    [InlineData("MelodicDictation", false)]
    [InlineData("RhythmDictation", false)]
    [InlineData("GuessRhythmPattern", false)]
    [InlineData("RhythmTap", false)]
    [InlineData("GuessMeter", false)]
    public async Task ExercisePage_OffersTheOctaveRange_OnlyWhenItsNotesComeFromIt(string exerciseName, bool offersTheRange)
    {
        ExerciseId(exerciseName);
        var client = await ClientAsync();

        var html = await client.GetStringAsync($"/Exercise/{exerciseName}");

        Buttons(html).Should().NotBeEmpty("every exercise offers the instruments");
        html.Contains("id=\"rangeFilter\"").Should().Be(offersTheRange);
        html.Contains("id=\"rangeStart\"").Should().Be(offersTheRange);
    }

    [Theory]
    [InlineData("GuessChords")]
    [InlineData("CompleteChord")]
    [InlineData("GuessTopNote")]
    public async Task ChordRound_OnTheViolin_IsPlayedOnThePiano(string exerciseName)
    {
        var exerciseId = ExerciseId(exerciseName);
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

    [Theory]
    [InlineData(null, GuitarPosition.Open, "E2", "D#3")]
    [InlineData("Open", GuitarPosition.Open, "E2", "D#3")]
    [InlineData("Barre", GuitarPosition.Barre, "F2", "B3")]
    [InlineData("High", GuitarPosition.High, "B2", "D4")]
    [InlineData("Drums", GuitarPosition.Open, "E2", "D#3")]
    public async Task ChordRound_OnTheGuitar_IsPlayedWhereOnTheNeckTheCookieSays(
        string? guitarPosition, GuitarPosition position, string lowest, string highest)
    {
        // The open chords have their basses from the low E string to the D#3 of x68886, the
        // barre chords from the F2 of 133211; the note range doesn't move them.
        var exerciseId = ExerciseId("GuessChords");
        var client = await ClientAsync(("instrument", "Guitar"), ("guitarPosition", guitarPosition), ("noteRange", "C5-C5"));

        for (var round = 0; round < 4; round++)
        {
            _factory.Mixer.Plans.Clear();

            var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

            play.GetProperty("playToken").GetString().Should().NotBeNullOrEmpty();
            var strum = _factory.Mixer.Plans.Should().ContainSingle().Subject.Select(input => GuitarNotes[input.SampleName]).ToList();
            strum.Should().Equal(GuitarVoicing.Find(strum, position)!.Notes, "the chord is strummed on its {0} shape", position);
            strum[0].Should().BeInRange(Midi(lowest), Midi(highest));
        }
    }

    private static readonly Dictionary<string, int> GuitarNotes =
        Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
            .ToDictionary(Instrument.Guitar.SampleName);

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    private sealed record Button(
        string Instrument, string Pressed, string Lowest, string Highest, string LowestNote, string Strummed, string Text);

    private static List<Button> Buttons(string html) =>
        [
            .. Regex.Matches(html,
                    "<button type=\"button\"\\s+data-instrument=\"(\\w+)\"\\s+data-lowest-octave=\"(\\d+)\"\\s+"
                    + "data-highest-octave=\"(\\d+)\"\\s+data-lowest-note=\"(\\w+)\"\\s+data-strummed=\"(\\w+)\"\\s+"
                    + "aria-pressed=\"(\\w+)\">([^<]*)</button>")
                .Select(m => new Button(m.Groups[1].Value, m.Groups[6].Value,
                    m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value, m.Groups[7].Value)),
        ];

    private sealed record Position(string Name, string Pressed, string Text);

    private static List<Position> Positions(string html) =>
        [
            .. Regex.Matches(html,
                    "<button type=\"button\"\\s+data-guitar-position=\"(\\w+)\"\\s+aria-pressed=\"(\\w+)\">([^<]*)</button>")
                .Select(m => new Position(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)),
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
