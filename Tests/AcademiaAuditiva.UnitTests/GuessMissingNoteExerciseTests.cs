using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Compare 2 melodies (GuessMissingNote) plays a melody, then the same melody again or with
/// one of its notes left out, and the student says whether they are the same. Every round that
/// expects "different" has to sound different, and the length filter sets the number of notes.
/// </summary>
public class GuessMissingNoteExerciseTests
{
    // The notes of a major scale, in semitones above its tonic.
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessMissingNote" };

    [Theory]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("6", 6)]
    [InlineData("7", 7)]
    [InlineData("8", 8)]
    [InlineData(null, 5)]
    [InlineData("abc", 5)]
    [InlineData("0", 4)]
    [InlineData("100", 8)]
    public void TheLengthFilter_SetsTheNumberOfNotes(string? melodyLength, int notes)
    {
        for (var i = 0; i < 50; i++)
        {
            var melody = (JArray)Round(melodyLength)["melody1"]!;

            melody.Should().HaveCount(notes)
                .And.AllSatisfy(entry => entry.Value<string>("type").Should().Be("note", "the melody has no rests"));
        }
    }

    [Fact]
    public void EveryDifferentRound_LeavesOutANoteInTheMiddle_AndTheOthersPlayTheMelodyAgain()
    {
        var answers = new HashSet<string>();

        for (var i = 0; i < 200; i++)
        {
            var round = Round("6");
            var melody1 = (JArray)round["melody1"]!;
            var melody2 = (JArray)round["melody2"]!;
            var answer = round.Value<string>("answer")!;
            answers.Add(answer);

            melody2.Should().HaveSameCount(melody1);
            var changed = Enumerable.Range(0, melody1.Count)
                .Where(k => !JToken.DeepEquals(melody1[k], melody2[k]))
                .ToList();
            if (answer == "same")
            {
                changed.Should().BeEmpty("a round that expects \"same\" plays the melody twice");
                continue;
            }

            answer.Should().Be("diff");
            var leftOut = changed.Should().ContainSingle("one note is left out").Subject;
            leftOut.Should().BeInRange(1, melody1.Count - 2, "a note left out at either end isn't heard as a gap");
            melody2[leftOut].Value<string>("type").Should().Be("rest");
            melody2[leftOut].Value<double>("duration").Should().Be(melody1[leftOut].Value<double>("duration"),
                "the silence keeps the pulse");
        }

        answers.Should().BeEquivalentTo("same", "diff");
    }

    [Fact]
    public void TheMelody_IsInAMajorKey_MovesByStepsAndThirds_AndEndsOnTheTonic()
    {
        var tonics = new HashSet<int>();

        for (var i = 0; i < 200; i++)
        {
            var melody = Round("8")["melody1"]!.Select(entry => Midi(entry.Value<string>("note")!)).ToList();
            var tonic = melody[^1];
            tonics.Add(tonic % 12);

            melody.Should().AllSatisfy(midi =>
            {
                MajorScale.Should().Contain((midi - tonic + 12) % 12, "the melody is in the major key of its last note");
                midi.Should().BeInRange(tonic - 5, tonic + 7, "it stays between the fifth degrees below and above the tonic");
                midi.Should().BeInRange(Midi("G3"), Midi("F#5"), "every instrument plays it");
            });
            melody.Zip(melody.Skip(1), (a, b) => Math.Abs(b - a)).Should()
                .OnlyContain(semitones => semitones >= 1 && semitones <= 4, "each note is a step or a third from the one before");
        }

        tonics.Should().HaveCount(12, "the key changes from round to round");
    }

    [Fact]
    public void TheMelody_IsInQuarterNotes_EndingOnAHalfNote()
    {
        var melody = (JArray)Round("5")["melody1"]!;

        melody.Select(entry => entry.Value<double>("duration")).Should().Equal(1.0, 1.0, 1.0, 1.0, 2.0);
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Guitar")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysBothMelodies_WithASilentBeatWhereTheNoteIsLeftOut(string instrument)
    {
        var planner = new ExercisePlaybackPlanner();
        var answers = new HashSet<string>();

        for (var i = 0; i < 50; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["melodyLength"] = "5", ["instrument"] = instrument });
            var answer = JObject.Parse(plan.ExpectedAnswerJson).Value<string>("answer")!;
            answers.Add(answer);

            plan.PlaybackPlans.Should().HaveCount(2, "each melody is a mix of its own");
            var (first, second) = (plan.PlaybackPlans[0], plan.PlaybackPlans[1]);
            // At 120 beats a minute: a quarter note every half second, then a half note.
            first.Select(input => input.StartTimeSeconds).Should().Equal(0.0, 0.5, 1.0, 1.5, 2.0);
            first.Select(input => input.DurationSeconds).Should().Equal(0.5, 0.5, 0.5, 0.5, 1.0);
            if (answer == "same")
            {
                second.Should().Equal(first);
                continue;
            }

            second.Should().HaveCount(4).And.OnlyContain(input => first.Contains(input),
                "the other notes are played at the same times");
            first.Except(second).Should().ContainSingle()
                .Which.StartTimeSeconds.Should().BeInRange(0.5, 1.5, "the note left out is neither the first nor the last");
        }

        answers.Should().BeEquivalentTo("same", "diff");
    }

    private static JObject Round(string? melodyLength)
    {
        var filters = new Dictionary<string, string>();
        if (melodyLength is not null)
            filters["melodyLength"] = melodyLength;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
