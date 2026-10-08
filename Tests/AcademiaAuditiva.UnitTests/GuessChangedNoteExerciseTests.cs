using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Which note changed? (GuessChangedNote) plays a melody, then the same melody with one of its
/// notes moved a step or a third along the scale, and the student says which note it was,
/// counting from the first, and whether it went up or down.
/// </summary>
public class GuessChangedNoteExerciseTests
{
    // The notes of a major scale, in semitones above its tonic.
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessChangedNote" };

    [Theory]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("8", 8)]
    [InlineData(null, 5)]
    [InlineData("abc", 5)]
    [InlineData("0", 4)]
    [InlineData("100", 8)]
    public void TheLengthFilter_SetsTheNumberOfNotes_OfBothMelodies(string? melodyLength, int notes)
    {
        for (var i = 0; i < 50; i++)
        {
            var round = Round(melodyLength);

            ((JArray)round["melody1"]!).Should().HaveCount(notes)
                .And.AllSatisfy(entry => entry.Value<string>("type").Should().Be("note", "the melody has no rests"));
            ((JArray)round["melody2"]!).Should().HaveCount(notes)
                .And.AllSatisfy(entry => entry.Value<string>("type").Should().Be("note"));
        }
    }

    [Fact]
    public void OneNote_MovesAStepOrAThirdAlongTheScale_AndTheAnswerSaysWhichAndWhere()
    {
        for (var i = 0; i < 300; i++)
        {
            var round = Round("8");
            var melody1 = Notes(round["melody1"]!);
            var melody2 = Notes(round["melody2"]!);
            var (position, up) = Answer(round);

            var changed = Enumerable.Range(0, melody1.Count).Where(k => melody1[k] != melody2[k]).ToList();
            changed.Should().ContainSingle("only one note changes").Which.Should().Be(position - 1, "it is the note of the answer");

            var tonic = melody1[^1];
            var scale = Enumerable.Range(tonic - 5, 13).Where(midi => MajorScale.Contains((midi - tonic + 12) % 12)).ToList();
            var (before, after) = (melody1[position - 1], melody2[position - 1]);
            scale.Should().Contain(after, "the note moves along the scale, between the fifth degrees below and above the tonic");
            after.Should().BeInRange(Midi("G3"), Midi("F#5"), "every instrument plays it");
            Math.Abs(scale.IndexOf(after) - scale.IndexOf(before)).Should().BeOneOf(new[] { 1, 2 }, "it moves a step or a third");
            (after > before).Should().Be(up, "the answer says whether it went up or down");
        }
    }

    [Fact]
    public void AnyNote_CanChange_UpOrDown_AByStepOrAThird()
    {
        var positions = new HashSet<int>();
        var directions = new HashSet<bool>();
        var moves = new HashSet<int>();

        for (var i = 0; i < 600; i++)
        {
            var round = Round("6");
            var (position, up) = Answer(round);
            positions.Add(position);
            directions.Add(up);
            moves.Add(Math.Abs(Notes(round["melody2"]!)[position - 1] - Notes(round["melody1"]!)[position - 1]) <= 2 ? 1 : 2);
        }

        positions.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6 }, "the first and the last notes can change too");
        directions.Should().BeEquivalentTo(new[] { true, false });
        moves.Should().BeEquivalentTo(new[] { 1, 2 }, "a step or a third");
    }

    [Fact]
    public void TheMelodies_AreInQuarterNotes_EndingOnAHalfNote()
    {
        var round = Round("5");

        round["melody1"]!.Select(entry => entry.Value<double>("duration")).Should().Equal(1.0, 1.0, 1.0, 1.0, 2.0);
        round["melody2"]!.Select(entry => entry.Value<double>("duration")).Should().Equal(1.0, 1.0, 1.0, 1.0, 2.0);
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Guitar")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysBothMelodies_AtTheSameTimes_WithOnlyTheChangedNoteDifferent(string instrument)
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 50; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["melodyLength"] = "5", ["instrument"] = instrument });
            var (position, up) = Answer(JObject.Parse(plan.ExpectedAnswerJson));

            plan.PlaybackPlans.Should().HaveCount(2, "each melody is a mix of its own");
            var (first, second) = (plan.PlaybackPlans[0], plan.PlaybackPlans[1]);
            // At 120 beats a minute: a quarter note every half second, then a half note.
            first.Select(input => input.StartTimeSeconds).Should().Equal(0.0, 0.5, 1.0, 1.5, 2.0);
            first.Select(input => input.DurationSeconds).Should().Equal(0.5, 0.5, 0.5, 0.5, 1.0);
            second.Select(input => input.StartTimeSeconds).Should().Equal(first.Select(input => input.StartTimeSeconds));
            second.Select(input => input.DurationSeconds).Should().Equal(first.Select(input => input.DurationSeconds));

            var differ = first.Zip(second).Where(pair => pair.First != pair.Second).ToList();
            var (was, now) = differ.Should().ContainSingle("only one note changes").Subject;
            was.StartTimeSeconds.Should().Be((position - 1) * 0.5, "it is the note of the answer");
            (SampleMidi(now.SampleName) > SampleMidi(was.SampleName)).Should().Be(up);
        }
    }

    private static JObject Round(string? melodyLength)
    {
        var filters = new Dictionary<string, string>();
        if (melodyLength is not null)
            filters["melodyLength"] = melodyLength;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static (int Position, bool Up) Answer(JObject round)
    {
        var answer = round.Value<string>("answer")!;
        answer.Should().MatchRegex(@"^[1-8]\|(up|down)$");
        var parts = answer.Split('|');
        return (int.Parse(parts[0]), parts[1] == "up");
    }

    private static List<int> Notes(JToken melody) => [.. melody.Select(entry => Midi(entry.Value<string>("note")!))];

    // "violin/As4.mp3" is A#4.
    private static int SampleMidi(string sample) => Midi(sample[(sample.LastIndexOf('/') + 1)..^".mp3".Length].Replace('s', '#'));

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
