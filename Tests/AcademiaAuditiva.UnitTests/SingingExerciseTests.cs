using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The singing exercises: SingNote plays a note, SingInterval a note and the interval to sing
/// from it, SingMelody a melody, and the student sings it back. The page sends the notes it
/// heard ("C4|G4"), which are checked in any octave.
/// </summary>
public class SingingExerciseTests
{
    private static readonly string[] Easy = ["2M", "3M", "4J", "5J", "8J"];

    // ---------- Rounds ----------

    [Fact]
    public void SingNote_DrawsEveryNoteOfTheRange()
    {
        var notes = new HashSet<int>();
        for (var i = 0; i < 400; i++)
        {
            var note = Midi(Round("SingNote", new() { ["noteRange"] = "C3-C3" }).Value<string>("note")!);
            note.Should().BeInRange(Midi("C3"), Midi("B3"));
            notes.Add(note);
        }

        notes.Should().HaveCount(12, "any note of the octave comes up");
    }

    [Theory]
    [InlineData(null, new[] { "2M", "3M", "4J", "5J", "8J" })]
    [InlineData("easy", new[] { "2M", "3M", "4J", "5J", "8J" })]
    [InlineData("medium", new[] { "2m", "2M", "3m", "3M", "4J", "5J", "6m", "6M", "8J" })]
    [InlineData("all", new[] { "2m", "2M", "3m", "3M", "4J", "4A", "5J", "6m", "6M", "7m", "7M", "8J" })]
    [InlineData("nonsense", new[] { "2M", "3M", "4J", "5J", "8J" })]
    public void SingInterval_TheLevelChoosesTheIntervals(string? level, string[] intervals)
    {
        var filters = new Dictionary<string, string>();
        if (level is not null)
            filters["siLevel"] = level;

        var drawn = Enumerable.Range(0, 500).Select(_ => Round("SingInterval", filters).Value<string>("interval")!).ToHashSet();

        drawn.Should().BeEquivalentTo(intervals);
    }

    [Theory]
    [InlineData(null, new[] { "asc" })]
    [InlineData("asc", new[] { "asc" })]
    [InlineData("desc", new[] { "desc" })]
    [InlineData("both", new[] { "asc", "desc" })]
    public void SingInterval_TheSecondNoteIsTheIntervalFromTheFirst_InTheDirectionOfTheFilter(string? direction, string[] directions)
    {
        var drawn = new HashSet<string>();
        for (var i = 0; i < 300; i++)
        {
            var filters = new Dictionary<string, string> { ["siLevel"] = "all", ["noteRange"] = "C4-C4" };
            if (direction is not null)
                filters["intervalDirection"] = direction;
            var round = Round("SingInterval", filters);
            var (first, second) = (Midi(round.Value<string>("note1")!), Midi(round.Value<string>("note2")!));
            var steps = MusicTheoryService.IntervalSemitones[round.Value<string>("interval")!];
            var drawnDirection = round.Value<string>("direction")!;
            drawn.Add(drawnDirection);

            first.Should().BeInRange(Midi("C4"), Midi("B4"), "the first note is played, so it comes from the range");
            (second - first).Should().Be(drawnDirection == "asc" ? steps : -steps);
        }

        drawn.Should().BeEquivalentTo(directions);
    }

    [Fact]
    public void SingInterval_Levels_UseIntervalsUpToTheOctave_EachOnce()
    {
        // SingInterval.js names an interval by its semitones, 1 to 12.
        MusicTheoryService.IntervalSemitones.Values.Should().BeEquivalentTo(Enumerable.Range(1, 12));
        MusicTheoryService.SingIntervalLevels.Keys.Should().BeEquivalentTo(["easy", "medium", "all"]);
        MusicTheoryService.SingIntervalLevels.Values.Should().AllSatisfy(level =>
            level.Should().OnlyHaveUniqueItems().And.OnlyContain(code => MusicTheoryService.IntervalSemitones.ContainsKey(code)));
        MusicTheoryService.SingIntervalLevels["easy"].Should().BeSubsetOf(MusicTheoryService.SingIntervalLevels["medium"]);
        MusicTheoryService.SingIntervalLevels["all"].Should().BeEquivalentTo(MusicTheoryService.IntervalSemitones.Keys);
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("8", 8)]
    [InlineData(null, 5)]
    public void SingMelody_TheLengthFilterSetsTheNumberOfNotes_InQuarterNotesEndingOnAHalfNote(string? melodyLength, int notes)
    {
        var filters = new Dictionary<string, string>();
        if (melodyLength is not null)
            filters["melodyLength"] = melodyLength;

        var melody = (JArray)Round("SingMelody", filters)["melody"]!;

        melody.Should().HaveCount(notes).And.AllSatisfy(entry => entry.Value<string>("type").Should().Be("note", "a melody to sing has no rests"));
        melody.Select(entry => entry.Value<double>("duration")).Should().Equal(
            Enumerable.Repeat(1.0, notes - 1).Append(2.0));
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysTheNote_TheFirstNoteOfTheInterval_OrTheMelody(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();
        var instrument = Instrument.All.Single(i => i.Name == instrumentName);

        for (var i = 0; i < 20; i++)
        {
            var note = planner.Plan(new Exercise { Name = "SingNote" }, new() { ["instrument"] = instrumentName });
            note.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle()
                .Which.SampleName.Should().Be(instrument.SampleFor(JObject.Parse(note.ExpectedAnswerJson).Value<string>("note")!));

            var interval = planner.Plan(new Exercise { Name = "SingInterval" }, new() { ["instrument"] = instrumentName });
            interval.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle("the second note is the student's to sing")
                .Which.SampleName.Should().Be(instrument.SampleFor(JObject.Parse(interval.ExpectedAnswerJson).Value<string>("note1")!));

            var melody = planner.Plan(new Exercise { Name = "SingMelody" }, new() { ["instrument"] = instrumentName, ["melodyLength"] = "6" });
            var notes = JObject.Parse(melody.ExpectedAnswerJson)["melody"]!.Select(entry => entry.Value<string>("note")!);
            var played = melody.PlaybackPlans.Should().ContainSingle().Subject;
            played.Select(input => input.SampleName).Should().Equal(notes.Select(instrument.SampleFor));
            // At 120 beats a minute: a quarter note every half second.
            played.Select(input => input.StartTimeSeconds).Should().Equal(0.0, 0.5, 1.0, 1.5, 2.0, 2.5);
        }
    }

    // ---------- Answers ----------

    [Fact]
    public void SungNotes_ReadsTheNotesSent_AndTheirPitchClasses()
    {
        SungNotes.Split(" C4 | E4 ||G4 ").Should().Equal("C4", "E4", "G4");
        SungNotes.Split("   ").Should().BeEmpty();
        SungNotes.Split(null).Should().BeEmpty();
        SungNotes.Split(string.Join("|", Enumerable.Repeat("C4", 400))).Should().BeEmpty("no recording has that many notes");

        SungNotes.PitchClass("C#3").Should().Be(1);
        SungNotes.PitchClass("Db").Should().Be(1, "a note without its octave still has a pitch class");
        SungNotes.PitchClass("Cb4").Should().Be(11);
        SungNotes.PitchClass("H4").Should().Be(-1);
        SungNotes.Midi("A4").Should().Be(69);
        SungNotes.Midi("A").Should().BeNull("an interval needs the octaves of its notes");
    }

    [Fact]
    public void SingNote_IsRightInAnyOctave_AndEnharmonicSpelling()
    {
        var v = new SingNoteValidator();
        var json = """{"note":"C#4"}""";

        v.ExerciseName.Should().Be("SingNote");
        v.Validate("C#4", json).Should().Be(new ExerciseValidationResult(true, "C#4"));
        v.Validate("C#2", json).IsCorrect.Should().BeTrue("any octave will do");
        v.Validate("Db5", json).IsCorrect.Should().BeTrue();
        v.Validate("D4", json).IsCorrect.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("C#4|C#4")]
    [InlineData("H4")]
    [InlineData("not a note")]
    public void SingNote_WantsOneNote(string guess)
    {
        new SingNoteValidator().Validate(guess, """{"note":"C#4"}""").Should().Be(new ExerciseValidationResult(false, "C#4"));
    }

    [Fact]
    public void SingInterval_WantsTheFirstNoteInAnyOctave_ThenTheInterval_InItsDirection()
    {
        var v = new SingIntervalValidator();
        var up = """{"note1":"C4","note2":"G4","interval":"5J","direction":"asc"}""";
        var down = """{"note1":"E4","note2":"C4","interval":"3M","direction":"desc"}""";

        v.ExerciseName.Should().Be("SingInterval");
        v.Validate("C4|G4", up).Should().Be(new ExerciseValidationResult(true, "C4|G4"));
        v.Validate("C3|G3", up).IsCorrect.Should().BeTrue("an octave lower is the same interval");
        v.Validate("B#2|G3", up).IsCorrect.Should().BeTrue("B#2 is C3");
        v.Validate("C3|G4", up).IsCorrect.Should().BeFalse("a fifth and an octave is not a fifth");
        v.Validate("C4|G3", up).IsCorrect.Should().BeFalse("a fourth down is not a fifth up");
        v.Validate("D4|A4", up).IsCorrect.Should().BeFalse("the first note is the one played");
        v.Validate("E3|C3", down).IsCorrect.Should().BeTrue();
        v.Validate("E3|G#3", down).IsCorrect.Should().BeFalse("a major third, but up");
    }

    [Theory]
    [InlineData("")]
    [InlineData("C4")]
    [InlineData("C4|G4|C5")]
    [InlineData("C|G")]
    [InlineData("C4|X4")]
    public void SingInterval_WantsTwoNotesWithTheirOctaves(string guess)
    {
        new SingIntervalValidator().Validate(guess, """{"note1":"C4","note2":"G4","interval":"5J","direction":"asc"}""")
            .Should().Be(new ExerciseValidationResult(false, "C4|G4"));
    }

    [Fact]
    public void SingMelody_IsCheckedAsSightSinging()
    {
        var v = new SingMelodyValidator();
        var json = """{"melody":[{"type":"note","note":"C4","duration":1.0},{"type":"note","note":"D4","duration":1.0},{"type":"note","note":"E4","duration":1.0},{"type":"note","note":"C4","duration":2.0}]}""";

        v.ExerciseName.Should().Be("SingMelody");
        v.Validate("C3|D3|E3|C3", json).Should().Be(new ExerciseValidationResult(true, "C4|D4|E4|C4"));
        v.Validate("C4|D4|E4|E4|C4", json).IsCorrect.Should().BeTrue("a held note counts once");
        v.Validate("C4|D4|F4|C4", json).IsCorrect.Should().BeTrue("one note of four may be wrong");
        v.Validate("C4|F4|G4|C4", json).IsCorrect.Should().BeFalse();
        v.Validate("", json).IsCorrect.Should().BeFalse();
    }

    [Theory]
    [InlineData("SingNote")]
    [InlineData("SingInterval")]
    [InlineData("SingMelody")]
    public void EachValidator_AcceptsTheAnswerOfTheRoundsItsExerciseDraws(string name)
    {
        IExerciseValidator validator = name switch
        {
            "SingNote" => new SingNoteValidator(),
            "SingInterval" => new SingIntervalValidator(),
            _ => new SingMelodyValidator(),
        };
        for (var i = 0; i < 100; i++)
        {
            var json = JsonConvert.SerializeObject(MusicTheoryService.GenerateNoteForExercise(
                new Exercise { Name = name }, new() { ["siLevel"] = "all", ["intervalDirection"] = "both" }));
            var answer = validator.Validate("", json).CanonicalAnswer;

            answer.Should().NotBeEmpty(json);
            validator.Validate(answer, json).IsCorrect.Should().BeTrue(json);
        }
    }


    private static JObject Round(string exercise, Dictionary<string, string> filters) =>
        JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(new Exercise { Name = exercise }, filters));

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
