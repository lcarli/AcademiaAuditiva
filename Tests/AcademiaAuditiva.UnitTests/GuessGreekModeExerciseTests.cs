using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the <c>GuessGreekMode</c> exercise wiring. Verifies that
/// the generator only ever picks one of the seven Greek modes, that each
/// mode produces a diatonic 8-note scale, and that the playback planner
/// emits a single sequential ascending plan.
/// </summary>
public class GuessGreekModeExerciseTests
{
    private static readonly string[] AllowedModes =
        { "ionian", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian" };

    private static Exercise NewExercise() => new Exercise
    {
        ExerciseId = 997,
        Name = "GuessGreekMode"
    };

    [Fact]
    public void Generator_PicksOneOfSevenModes_AndProducesEightNotes()
    {
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string>());

            var json = JObject.FromObject(raw);
            var mode = (string?)json["mode"];
            var notes = json["notes"] as JArray;

            mode.Should().BeOneOf(AllowedModes);
            notes.Should().NotBeNull();
            // Greek modes are all diatonic 7-step scales — 8 notes incl. octave.
            notes!.Count.Should().Be(8, $"mode={mode}");
        }
    }

    [Fact]
    public void Generator_HonoursExplicitRoot_WhenFilterIsProvided()
    {
        var exercise = NewExercise();

        var raw = MusicTheoryService.GenerateNoteForExercise(
            exercise,
            new Dictionary<string, string> { { "scaleRoot", "D" }, { "scaleOctave", "4" } });

        var json = JObject.FromObject(raw);
        var notes = json["notes"] as JArray;
        notes.Should().NotBeNull();
        ((string?)notes![0]).Should().Be("D4",
            "the mode must start on the requested root in the requested octave");
    }

    [Fact]
    public void PlaybackPlanner_EmitsAscendingSequentialPlan()
    {
        var planner = new ExercisePlaybackPlanner();
        var plan = planner.Plan(
            NewExercise(),
            new Dictionary<string, string> { { "scaleRoot", "C" }, { "scaleOctave", "4" } });

        plan.PlaybackPlans.Should().HaveCount(1);
        var sequence = plan.PlaybackPlans[0];
        sequence.Count.Should().Be(8);

        for (var i = 1; i < sequence.Count; i++)
        {
            sequence[i].StartTimeSeconds.Should().BeGreaterThan(
                sequence[i - 1].StartTimeSeconds,
                $"note at index {i} must start after the previous note");
        }
    }
}
