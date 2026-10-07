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
/// GuessTopNote plays a major or minor triad in four voices, the root in the bass and three
/// voices in close position above it, and asks which tone of the chord is on top: the root, the
/// third or the fifth. The bass is a note of the range, and the chord goes down by octaves while
/// its top is past the instrument's highest note. It is played as written, the guitar strumming
/// exactly its notes, since the voicing is the question.
/// </summary>
public class GuessTopNoteExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 998, Name = "GuessTopNote" };

    private static readonly string[] TopNotes = ["topRoot", "topThird", "topFifth"];

    private static readonly Dictionary<string, int> Thirds = new() { ["major"] = 4, ["minor"] = 3 };

    // The voices of each answer in semitones above the bass, for a third of <third> semitones.
    private static int[] Voicing(string topNote, int third) => topNote switch
    {
        "topRoot" => [0, 12 + third, 19, 24],
        "topThird" => [0, 7, 12, 12 + third],
        "topFifth" => [0, 12, 12 + third, 19],
        _ => throw new ArgumentOutOfRangeException(nameof(topNote), topNote, "GuessTopNote asks no such tone"),
    };

    // The letter of each voice, in steps above the bass's: the root, the third or the fifth.
    private static int[] Letters(string topNote) => topNote switch
    {
        "topRoot" => [0, 2, 4, 0],
        "topThird" => [0, 4, 0, 2],
        "topFifth" => [0, 0, 2, 4],
        _ => throw new ArgumentOutOfRangeException(nameof(topNote), topNote, "GuessTopNote asks no such tone"),
    };

    [Fact]
    public void EveryTopNote_IsAsked_AndEachHasAButton()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-top-note-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessTopNote").AnswerButtonsJson!)!["guessAnswer"];

        var asked = Enumerable.Range(0, 200).Select(_ => Round([]).Value<string>("topNote")).Distinct();

        buttons.Values.Should().BeEquivalentTo(TopNotes);
        asked.Should().BeEquivalentTo(buttons.Values, "the student can give every answer, and every button can be right");
    }

    [Theory]
    [InlineData("major", new[] { "major" })]
    [InlineData("minor", new[] { "minor" })]
    [InlineData("both", new[] { "major", "minor" })]
    [InlineData(null, new[] { "major", "minor" })]
    [InlineData("sevenths", new[] { "major", "minor" })]
    public void TheQualityFilter_PicksTheChords(string? quality, string[] qualities)
    {
        var filters = new Dictionary<string, string>();
        if (quality is not null)
            filters["tnQuality"] = quality;

        var played = Enumerable.Range(0, 100).Select(_ => Round(filters).Value<string>("quality")).Distinct();

        played.Should().BeEquivalentTo(qualities);
    }

    [Theory]
    [InlineData("Piano", "C1-C6")]
    [InlineData("Guitar", "C2-C5")]
    public void TheChord_IsTheRootInTheBass_AndThreeVoicesInClosePosition_TheAnswerOnTop(string instrumentName, string noteRange)
    {
        var instrument = Instrument.FromName(instrumentName);

        for (var i = 0; i < 300; i++)
        {
            var round = Round(new() { ["tnQuality"] = "both", ["noteRange"] = noteRange }, instrument);
            var notes = Midis(round["notes"]!);
            var topNote = round.Value<string>("topNote")!;

            notes.Select(midi => midi - notes[0]).Should().Equal(Voicing(topNote, Thirds[round.Value<string>("quality")!]),
                "{0} is a {1} chord with its {2}", round.ToString(Formatting.None), round["quality"], topNote);
        }
    }

    // A note keeps its spelling when the chord moves by octaves: the third of D# major is F##.
    [Fact]
    public void TheNotes_AreSpelledAsTheTonesOfTheChord()
    {
        for (var i = 0; i < 300; i++)
        {
            var round = Round(new() { ["tnQuality"] = "both", ["noteRange"] = "C1-C6" });
            var names = round["notes"]!.Values<string>().Select(name => name!).ToList();

            names.Select(name => (Letter(name) - Letter(names[0]) + 7) % 7).Should().Equal(Letters(round.Value<string>("topNote")!),
                "{0} spells the tones of its chord", round.ToString(Formatting.None));
        }
    }

    [Theory]
    [InlineData("Piano", "C1-C1", 1)]
    [InlineData("Piano", "C4-C4", 4)]
    [InlineData("Guitar", "C2-C2", 2)]
    [InlineData("Guitar", "C3-C3", 3)]
    public void TheBass_IsANoteOfTheRange(string instrumentName, string noteRange, int octave)
    {
        var instrument = Instrument.FromName(instrumentName);
        var lowest = Math.Max((octave + 1) * 12, instrument.LowestMidi);

        for (var i = 0; i < 100; i++)
        {
            var bass = Midis(Round(new() { ["noteRange"] = noteRange }, instrument)["notes"]!)[0];

            bass.Should().BeInRange(lowest, (octave + 1) * 12 + 11, "the bass of the {0} is in octave {1}", instrumentName, octave);
        }
    }

    // The chord spans two octaves: from the top octave of the range it goes past the piano's B6
    // and the guitar's B5, so it goes down, only as far as it has to.
    [Theory]
    [InlineData("Piano", "C6-C6")]
    [InlineData("Guitar", "C5-C5")]
    public void InTheTopOctave_TheChordGoesDownByOctaves_UntilItsTopIsOnTheInstrument(string instrumentName, string noteRange)
    {
        var instrument = Instrument.FromName(instrumentName);

        for (var i = 0; i < 100; i++)
        {
            var round = Round(new() { ["tnQuality"] = "both", ["noteRange"] = noteRange }, instrument);
            var notes = Midis(round["notes"]!);

            notes.Should().AllSatisfy(midi => midi.Should().BeInRange(instrument.LowestMidi, instrument.HighestMidi,
                "the {0} plays {1}", instrumentName, round.ToString(Formatting.None)));
            (notes[^1] + 12).Should().BeGreaterThan(instrument.HighestMidi, "{0} is as high as the {1} plays it", round.ToString(Formatting.None), instrumentName);
            notes.Select(midi => midi - notes[0]).Should().Equal(Voicing(round.Value<string>("topNote")!, Thirds[round.Value<string>("quality")!]));
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void TheChord_IsPlayedAsWritten_AllItsNotesTogether(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 30; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["tnQuality"] = "both" });

            var notes = JObject.Parse(plan.ExpectedAnswerJson)["notes"]!.Values<string>();
            plan.PlaybackPlans.Should().ContainSingle().Which.Should().Equal(
                notes.Select(note => new MixInput(Instrument.Piano.SampleFor(note!), 0.0, 2.0)),
                "the chord rings for a whole note, on the piano when the instrument plays one note at a time");
        }
    }

    [Fact]
    public void OnTheGuitar_TheChordIsStrummedAsWritten_FromTheBassUp()
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 30; i++)
        {
            // The position the other chord exercises play on doesn't move it to a shape of the neck.
            var plan = planner.Plan(Exercise, new()
            {
                ["instrument"] = "Guitar", ["guitarPosition"] = "High", ["noteRange"] = "C2-C5",
            });

            var notes = Midis(JObject.Parse(plan.ExpectedAnswerJson)["notes"]!);
            var strum = plan.PlaybackPlans.Should().ContainSingle().Subject;
            strum.Select(input => input.SampleName).Should().Equal(notes.Select(Instrument.Guitar.SampleName),
                "the student hears the voicing they name the top note of");
            for (var k = 0; k < strum.Count; k++)
            {
                strum[k].StartTimeSeconds.Should().BeApproximately(k * 0.015, 1e-9);
                (strum[k].StartTimeSeconds + strum[k].DurationSeconds).Should().BeApproximately(2.0, 1e-9,
                    "every string rings until the whole note ends");
            }
        }
    }

    private static JObject Round(Dictionary<string, string> filters, Instrument? instrument = null) =>
        JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters, instrument));

    private static List<int> Midis(JToken notes) => [.. notes.Values<string>().Select(note => Midi(note!))];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    private static int Letter(string note) => "CDEFGAB".IndexOf(note[0], StringComparison.Ordinal);
}
