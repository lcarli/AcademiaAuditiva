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
/// GuessDegree asks which degree of a key a note is. Each round first sets the key with the
/// cadence GuessFunction plays, I–IV–V–I in major and i–iv–V–i in minor, then a silent beat,
/// then the note. The degrees are counted on the key's own scale, the natural minor in a minor
/// key, and the chromatic level adds the notes between them as raised (#) or lowered (b) degrees.
/// </summary>
public class GuessDegreeExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessDegree" };

    [Theory]
    [InlineData("C", "major", "C4 E4 G4", "F4 A4 C5", "G4 B4 D5")]
    [InlineData("F#", "major", "F#4 A#4 C#5", "B4 D#5 F#5", "C#5 E#5 G#5")]
    [InlineData("A", "minor", "A4 C5 E5", "D5 F5 A5", "E5 G#5 B5")]
    [InlineData("A#", "minor", "A#4 C#5 E#5", "D#5 F#5 A#5", "E#5 G##5 B#5")]
    public void EveryRound_StartsWithTheCadenceOfItsKey(string key, string scale, string tonic, string subdominant, string dominant)
    {
        foreach (var level in new[] { "diatonic", "chromatic" })
        {
            for (var i = 0; i < 20; i++)
            {
                var cadence = Round(key, scale, level)["cadence"]!.Select(Midis).ToList();

                cadence.Should().HaveCount(4);
                cadence[0].Should().Equal(MidisOf(tonic));
                cadence[1].Should().Equal(MidisOf(subdominant));
                cadence[2].Should().Equal(MidisOf(dominant), "the dominant has the leading tone, in minor too");
                cadence[3].Should().Equal(MidisOf(tonic), "the cadence comes back to the tonic");
            }
        }
    }

    // Each degree, and the semitones from the tonic up to it.
    [Theory]
    [InlineData("major", "diatonic", "1:0 2:2 3:4 4:5 5:7 6:9 7:11")]
    [InlineData("major", "chromatic", "1:0 b2:1 2:2 b3:3 3:4 4:5 #4:6 5:7 b6:8 6:9 b7:10 7:11")]
    [InlineData("minor", "diatonic", "1:0 2:2 3:3 4:5 5:7 6:8 7:10")]
    [InlineData("minor", "chromatic", "1:0 b2:1 2:2 3:3 #3:4 4:5 #4:6 5:7 6:8 #6:9 7:10 #7:11")]
    public void TheNote_IsItsDegreeOfTheKey_AndEveryDegreeIsAsked(string scale, string level, string degrees)
    {
        var semitones = degrees.Split(' ').Select(degree => degree.Split(':')).ToDictionary(d => d[0], d => int.Parse(d[1]));

        var rounds = Enumerable.Range(0, 400).Select(_ => Round("any", scale, level, "C2-C5")).ToList();

        foreach (var round in rounds)
        {
            var answer = round.Value<string>("answer")!;
            semitones.Should().ContainKey(answer);
            var tonic = Midi((string)round["cadence"]![0]![0]!);
            PitchClass(Midi(round.Value<string>("note")!) - tonic).Should().Be(semitones[answer],
                "{0} is {1} semitones up from the tonic of a {2} key", answer, semitones[answer], scale);
        }
        rounds.Select(round => round.Value<string>("answer")).Distinct().Should().BeEquivalentTo(semitones.Keys);
    }

    [Theory]
    [InlineData("C", "major", "diatonic", "7", "B")]
    [InlineData("C", "major", "chromatic", "b3", "Eb")]
    [InlineData("F#", "major", "chromatic", "#4", "C")]
    [InlineData("A", "minor", "diatonic", "3", "C")]
    [InlineData("A", "minor", "diatonic", "6", "F")]
    [InlineData("A", "minor", "diatonic", "7", "G")]
    [InlineData("A", "minor", "chromatic", "#3", "C#")]
    [InlineData("A", "minor", "chromatic", "#7", "G#")]
    public void EachDegree_IsItsNoteInTheKey(string key, string scale, string level, string degree, string note)
    {
        var rounds = Enumerable.Range(0, 300).Select(_ => Round(key, scale, level))
            .Where(round => round.Value<string>("answer") == degree)
            .ToList();

        // A minor key counts on its natural minor: in A minor, 7 is G, and the leading tone,
        // G#, is the raised 7 of the chromatic level.
        rounds.Should().NotBeEmpty("{0} is asked in {1} {2}", degree, key, scale);
        rounds.Should().AllSatisfy(round =>
            PitchClass(Midi(round.Value<string>("note")!)).Should().Be(PitchClass(Midi(note + "4"))));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("H", "dorian", "expert")]
    [InlineData("Bxxxx", "major", "")]
    public void AnUnknownKeyScaleOrLevel_IsCMajor_Diatonic(string? key, string? scale, string? level)
    {
        var rounds = Enumerable.Range(0, 200).Select(_ => Round(key, scale, level)).ToList();

        rounds.Should().AllSatisfy(round => round["cadence"]!.Select(Midis).Should().BeEquivalentTo(
            new[] { MidisOf("C4 E4 G4"), MidisOf("F4 A4 C5"), MidisOf("G4 B4 D5"), MidisOf("C4 E4 G4") },
            options => options.WithStrictOrdering()));
        rounds.Select(round => round.Value<string>("answer")).Distinct()
            .Should().BeEquivalentTo(new[] { "1", "2", "3", "4", "5", "6", "7" });
    }

    [Fact]
    public void AnyKey_DrawsTheKeyOfEachQuestion()
    {
        var tonics = Enumerable.Range(0, 300)
            .Select(_ => PitchClass(Midi((string)Round("any", "major", "diatonic")["cadence"]![0]![0]!)))
            .ToHashSet();

        tonics.Should().HaveCount(12, "every key of the filter can be drawn");
    }

    [Fact]
    public void EveryAnswer_HasAButton_AndEveryButtonIsAnAnswer()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-degree-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessDegree").AnswerButtonsJson!)!["guessAnswer"];

        var answers = new[] { "major", "minor" }
            .SelectMany(scale => new[] { "diatonic", "chromatic" }.Select(level => (scale, level)))
            .SelectMany(kind => Enumerable.Range(0, 300).Select(_ => Round("E", kind.scale, kind.level).Value<string>("answer")!))
            .ToHashSet();

        buttons.Values.Should().OnlyHaveUniqueItems("the keys and levels share their degrees")
            .And.BeEquivalentTo(answers, "the student can give every answer, and every button can be right");
    }

    // The range sets the octave of the key; the note goes into the notes of the instrument, and
    // the cadence in the top octave of the piano down an octave, under the highest sample.
    [Theory]
    [InlineData("Piano", "C1-C1")]
    [InlineData("Piano", "C6-C6")]
    [InlineData("Guitar", "C1-C1")]
    [InlineData("Guitar", "C6-C6")]
    [InlineData("Violin", "C1-C1")]
    [InlineData("Violin", "C6-C6")]
    public void TheNote_IsOneTheInstrumentPlays_AndTheCadenceHasSamples(string instrumentName, string noteRange)
    {
        var instrument = Instrument.FromName(instrumentName);
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            for (var i = 0; i < 40; i++)
            {
                var plan = planner.Plan(Exercise, new()
                {
                    ["instrument"] = instrumentName, ["noteRange"] = noteRange,
                    ["keySelect"] = "any", ["scaleTypeSelect"] = scale, ["gdLevel"] = "chromatic",
                });

                var round = JObject.Parse(plan.ExpectedAnswerJson);
                instrument.Has(round.Value<string>("note")!).Should().BeTrue(
                    "the {0} plays {1}", instrumentName, round.Value<string>("note"));
                round["cadence"]!.SelectMany(Midis).Should().OnlyContain(midi => PianoSamples.Covers(midi));
            }
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysTheCadence_ASilentBeat_ThenTheNote(string instrumentName)
    {
        var instrument = Instrument.FromName(instrumentName);
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            for (var i = 0; i < 10; i++)
            {
                var plan = planner.Plan(Exercise, new()
                {
                    ["instrument"] = instrumentName, ["keySelect"] = "G", ["scaleTypeSelect"] = scale, ["gdLevel"] = "chromatic",
                });

                // At GuessCadence's pace, a chord every 1.25 s; the violin leaves the chords to the piano.
                var round = JObject.Parse(plan.ExpectedAnswerJson);
                var cadence = round["cadence"]!.SelectMany((cadenceChord, k) => cadenceChord.Values<string>()
                    .Select(note => new MixInput(Instrument.Piano.SampleFor(note!), k * 1.25, 1.2)));
                var note = new MixInput(instrument.SampleFor(round.Value<string>("note")!), 6.25, 1.5);
                plan.PlaybackPlans.Should().ContainSingle("Replay plays the key again before the note")
                    .Which.Should().Equal(cadence.Append(note));
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Barre")]
    [InlineData("High")]
    public void OnTheGuitar_TheCadenceIsStrummedOnTheOpenChords_ThenTheNoteIsPlayed(string? guitarPosition)
    {
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            var filters = new Dictionary<string, string>
            {
                ["instrument"] = "Guitar", ["keySelect"] = "any", ["scaleTypeSelect"] = scale, ["gdLevel"] = "chromatic",
            };
            // The page offers no position: the student picks it for the exercises that play chords.
            if (guitarPosition is not null)
                filters["guitarPosition"] = guitarPosition;

            for (var i = 0; i < 30; i++)
            {
                var plan = planner.Plan(Exercise, filters);

                var round = JObject.Parse(plan.ExpectedAnswerJson);
                var inputs = plan.PlaybackPlans.Should().ContainSingle().Subject;
                var strums = inputs.Where(input => input.StartTimeSeconds < 6.25)
                    .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                    .ToList();
                var cadence = round["cadence"]!.Select(Midis).ToList();
                strums.Select(strum => strum.Key).Should().Equal(0, 1, 2, 3);
                for (var k = 0; k < cadence.Count; k++)
                {
                    strums[k].Select(input => input.SampleName).Should().Equal(
                        GuitarVoicing.Find(cadence[k], GuitarPosition.Open)!.Notes.Select(Instrument.Guitar.SampleName));
                }
                inputs.Where(input => input.StartTimeSeconds >= 6.25).Should().Equal(
                    new MixInput(Instrument.Guitar.SampleFor(round.Value<string>("note")!), 6.25, 1.5));
            }
        }
    }

    private static JObject Round(string? key, string? scale, string? level, string? noteRange = null)
    {
        var filters = new Dictionary<string, string>();
        if (key is not null)
            filters["keySelect"] = key;
        if (scale is not null)
            filters["scaleTypeSelect"] = scale;
        if (level is not null)
            filters["gdLevel"] = level;
        if (noteRange is not null)
            filters["noteRange"] = noteRange;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static List<int> Midis(JToken chord) => [.. chord.Values<string>().Select(note => Midi(note!))];

    private static List<int> MidisOf(string notes) => [.. notes.Split(' ').Select(Midi)];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    private static int PitchClass(int semitones) => ((semitones % 12) + 12) % 12;
}
