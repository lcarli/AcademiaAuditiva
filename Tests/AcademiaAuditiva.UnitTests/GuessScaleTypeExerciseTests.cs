using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the <c>GuessScaleType</c> exercise wiring. Verifies that
/// the generator only ever picks one of the four documented scale
/// families, that the produced note count matches the family
/// (7 for diatonic, 6 for pentatonic — head + 5/6 steps), and that the
/// playback planner emits a single sequential plan in pitch order.
/// </summary>
public class GuessScaleTypeExerciseTests
{
    private static readonly string[] AllowedScaleTypes =
        { "major", "minor", "majorPentatonic", "minorPentatonic" };

    private static Exercise NewExercise() => new Exercise
    {
        ExerciseId = 998,
        Name = "GuessScaleType"
    };

    [Fact]
    public void Generator_PicksOneOfFourScaleFamilies_AndProducesMatchingNoteCount()
    {
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string>());

            var json = JObject.FromObject(raw);
            var scaleType = (string?)json["scaleType"];
            var notes = json["notes"] as JArray;

            scaleType.Should().BeOneOf(AllowedScaleTypes);
            notes.Should().NotBeNull();
            notes!.Count.Should().BeGreaterOrEqualTo(6,
                "even pentatonic scales must yield at least head + 5 step notes");

            // Pentatonic = 6 notes total (root + 5 intervals), diatonic = 8.
            var expected = scaleType is "majorPentatonic" or "minorPentatonic" ? 6 : 8;
            notes.Count.Should().Be(expected, $"scaleType={scaleType}");
        }
    }

    [Fact]
    public void Generator_HonoursExplicitRoot_WhenFilterIsProvided()
    {
        var exercise = NewExercise();

        var raw = MusicTheoryService.GenerateNoteForExercise(
            exercise,
            new Dictionary<string, string> { { "scaleRoot", "G" }, { "scaleOctave", "4" } });

        var json = JObject.FromObject(raw);
        var notes = json["notes"] as JArray;
        notes.Should().NotBeNull();
        ((string?)notes![0]).Should().Be("G4",
            "the scale must start on the requested root in the requested octave");
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
        sequence.Count.Should().BeGreaterOrEqualTo(6);

        // Each note must start strictly after the previous one — a scale
        // is played one note at a time, never as a chord.
        for (var i = 1; i < sequence.Count; i++)
        {
            sequence[i].StartTimeSeconds.Should().BeGreaterThan(
                sequence[i - 1].StartTimeSeconds,
                $"note at index {i} must start after the previous note");
        }
    }
}
