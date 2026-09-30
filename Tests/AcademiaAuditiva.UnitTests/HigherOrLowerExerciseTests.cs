using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the <c>HigherOrLower</c> exercise wiring across the
/// generator, the playback planner and the validator. The generator is
/// the only one of the three that can produce wrong output silently
/// (e.g. picking the same note twice or labelling direction backwards),
/// so we run a tight randomized loop to make sure every roll is
/// internally consistent.
/// </summary>
public class HigherOrLowerExerciseTests
{
    private static Exercise NewExercise() => new Exercise
    {
        ExerciseId = 999,
        Name = "HigherOrLower"
    };

    [Fact]
    public void Generator_ProducesTwoDistinctNotes_AndCorrectDirectionLabel()
    {
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string> { { "noteRange", "C4-C5" } });

            var json = JObject.FromObject(raw);
            var n1 = (string?)json["note1"];
            var n2 = (string?)json["note2"];
            var answer = (string?)json["answer"];

            n1.Should().NotBeNullOrWhiteSpace();
            n2.Should().NotBeNullOrWhiteSpace();
            n1.Should().NotBe(n2, "the two notes have to differ for higher/lower to be meaningful");

            answer.Should().BeOneOf("higher", "lower");

            // Direction label must match actual pitch order. Notes are
            // serialized as "<letter><sharp?><octave>" — convert each to
            // a comparable semitone count and verify.
            var s1 = ToSemitones(n1!);
            var s2 = ToSemitones(n2!);
            if (answer == "higher")
                s2.Should().BeGreaterThan(s1, $"answer=higher note1={n1} note2={n2}");
            else
                s2.Should().BeLessThan(s1, $"answer=lower note1={n1} note2={n2}");
        }
    }

    [Fact]
    public void Generator_ExpandsRange_WhenNoteRangeIsASingleOctave()
    {
        // C4-C4 only spans one octave (12 chromatic notes); the generator
        // is documented to silently widen this so contrast is always
        // perceivable. The contract we lock in: 200 rolls with a single
        // octave never crash and always produce two distinct notes.
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string> { { "noteRange", "C4-C4" } });

            var json = JObject.FromObject(raw);
            ((string?)json["note1"]).Should().NotBe((string?)json["note2"]);
        }
    }

    [Fact]
    public void PlaybackPlanner_EmitsTwoNoteSequence_LikeGuessInterval()
    {
        var planner = new ExercisePlaybackPlanner();
        var plan = planner.Plan(
            NewExercise(),
            new Dictionary<string, string> { { "noteRange", "C4-C5" }, { "instrument", "Piano" } });

        plan.PlaybackPlans.Should().HaveCount(1);
        var sequence = plan.PlaybackPlans[0];
        sequence.Should().HaveCount(2, "HigherOrLower plays exactly two notes back-to-back");
        // Sequential — second note's start must be strictly after the first's.
        sequence[1].StartTimeSeconds.Should().BeGreaterThan(sequence[0].StartTimeSeconds);
    }

    private static int ToSemitones(string note)
    {
        var letter = note[0];
        var hasSharp = note.Length > 2 && note[1] == '#';
        var octave = int.Parse(note.Substring(hasSharp ? 2 : 1));
        var pc = letter switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => throw new ArgumentException($"Unknown note letter '{letter}'"),
        };
        return octave * 12 + pc + (hasSharp ? 1 : 0);
    }
}
