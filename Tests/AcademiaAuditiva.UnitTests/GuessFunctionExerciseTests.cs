using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessFunction asks what a chord does in a key, so each round first sets the key: a cadence,
/// I–IV–V–I in major and i–iv–V–i in minor, then a silent beat, then the chord. A minor key takes
/// its dominant (V) and its leading-tone chord (vii°) from the harmonic minor, as tonal music does.
/// </summary>
public class GuessFunctionExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessFunction" };

    [Theory]
    [InlineData("C", "major", "C4 E4 G4", "F4 A4 C5", "G4 B4 D5")]
    [InlineData("F#", "major", "F#4 A#4 C#5", "B4 D#5 F#5", "C#5 E#5 G#5")]
    [InlineData("A", "minor", "A4 C5 E5", "D5 F5 A5", "E5 G#5 B5")]
    [InlineData("A#", "minor", "A#4 C#5 E#5", "D#5 F#5 A#5", "E#5 G##5 B#5")]
    public void EveryRound_StartsWithTheCadenceOfItsKey(string key, string scale, string tonic, string subdominant, string dominant)
    {
        for (var i = 0; i < 20; i++)
        {
            var cadence = Round(key, scale)["cadence"]!.Select(Midis).ToList();

            cadence.Should().HaveCount(4);
            cadence[0].Should().Equal(MidisOf(tonic));
            cadence[1].Should().Equal(MidisOf(subdominant));
            cadence[2].Should().Equal(MidisOf(dominant), "the dominant has the leading tone, in minor too");
            cadence[3].Should().Equal(MidisOf(tonic), "the cadence comes back to the tonic");
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("H", "dorian")]
    [InlineData("Bxxxx", "major")]
    public void AnUnknownKeyOrScale_IsCMajor(string? key, string? scale)
    {
        var round = Round(key, scale);

        round["cadence"]!.Select(Midis).Should().BeEquivalentTo(
            new[] { MidisOf("C4 E4 G4"), MidisOf("F4 A4 C5"), MidisOf("G4 B4 D5"), MidisOf("C4 E4 G4") },
            options => options.WithStrictOrdering());
        round.Value<string>("answer").Should().BeOneOf("1-major", "2-minor", "3-minor", "4-major", "5-major", "6-minor", "7-diminished");
    }

    [Theory]
    [InlineData("C", "major", "1-major", "C4 E4 G4")]
    [InlineData("C", "major", "2-minor", "D4 F4 A4")]
    [InlineData("C", "major", "3-minor", "E4 G4 B4")]
    [InlineData("C", "major", "4-major", "F4 A4 C5")]
    [InlineData("C", "major", "5-major", "G4 B4 D5")]
    [InlineData("C", "major", "6-minor", "A4 C5 E5")]
    [InlineData("C", "major", "7-diminished", "B4 D5 F5")]
    [InlineData("A", "minor", "1-minor", "A4 C5 E5")]
    [InlineData("A", "minor", "2-diminished", "B4 D5 F5")]
    [InlineData("A", "minor", "3-major", "C5 E5 G5")]
    [InlineData("A", "minor", "4-minor", "D5 F5 A5")]
    [InlineData("A", "minor", "5-major", "E5 G#5 B5")]
    [InlineData("A", "minor", "6-major", "F5 A5 C6")]
    [InlineData("A", "minor", "7-diminished", "G#5 B5 D6")]
    public void EachFunction_IsTheTriadOnItsDegreeOfTheKey(string key, string scale, string function, string chord)
    {
        var rounds = Enumerable.Range(0, 300).Select(_ => Round(key, scale))
            .Where(round => round.Value<string>("answer") == function)
            .ToList();

        rounds.Should().NotBeEmpty("{0} is asked in {1} {2}", function, key, scale);
        rounds.Should().AllSatisfy(round => round["notes"]!.Select(note => Midi((string)note!)).Should().Equal(MidisOf(chord)));
    }

    [Theory]
    [InlineData("major", "1-major 2-minor 3-minor 4-major 5-major 6-minor 7-diminished")]
    [InlineData("minor", "1-minor 2-diminished 3-major 4-minor 5-major 6-major 7-diminished")]
    public void TheAnswers_AreTheSevenChordsOfTheKey(string scale, string functions)
    {
        var answers = Enumerable.Range(0, 300).Select(_ => Round("D", scale).Value<string>("answer")).ToHashSet();

        answers.Should().BeEquivalentTo(functions.Split(' '));
    }

    [Fact]
    public void EveryAnswer_HasAButton_AndEveryButtonIsAnAnswer()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-function-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessFunction").AnswerButtonsJson!)!["guessAnswer"];

        var answers = new[] { "major", "minor" }
            .SelectMany(scale => Enumerable.Range(0, 300).Select(_ => Round("E", scale).Value<string>("answer")!))
            .ToHashSet();

        buttons.Values.Should().OnlyHaveUniqueItems("the major and minor keys share V and vii°")
            .And.BeEquivalentTo(answers, "the student can give every answer, and every button can be right");
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysTheCadence_ASilentBeat_ThenTheChord(string instrument)
    {
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            for (var i = 0; i < 10; i++)
            {
                var plan = planner.Plan(Exercise, new() { ["instrument"] = instrument, ["keySelect"] = "G", ["scaleTypeSelect"] = scale });

                // At GuessCadence's pace, a chord every 1.25 s; the violin leaves chords to the piano.
                var round = JObject.Parse(plan.ExpectedAnswerJson);
                var cadence = round["cadence"]!.SelectMany((cadenceChord, k) => cadenceChord.Values<string>()
                    .Select(note => new MixInput(Instrument.Piano.SampleFor(note!), k * 1.25, 1.2)));
                var chord = round["notes"]!.Values<string>()
                    .Select(note => new MixInput(Instrument.Piano.SampleFor(note!), 6.25, 1.5));
                plan.PlaybackPlans.Should().ContainSingle("Replay plays the key again before the chord")
                    .Which.Should().Equal(cadence.Concat(chord));
            }
        }
    }

    private static JObject Round(string? key, string? scale)
    {
        var filters = new Dictionary<string, string>();
        if (key is not null)
            filters["keySelect"] = key;
        if (scale is not null)
            filters["scaleTypeSelect"] = scale;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static List<int> Midis(JToken chord) => [.. chord.Values<string>().Select(note => Midi(note!))];

    private static List<int> MidisOf(string notes) => [.. notes.Split(' ').Select(Midi)];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
