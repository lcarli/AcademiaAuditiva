using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the <c>GuessInversion</c> exercise wiring. Verifies that
/// the generator picks one of the three triad inversions (root / first /
/// second), produces a 3-note chord, and that the playback planner
/// stacks all 3 notes at the same start time so they ring as a true
/// chord rather than a sequence.
/// </summary>
public class GuessInversionExerciseTests
{
    private static readonly string[] AllowedInversions = { "root", "first", "second" };

    private static Exercise NewExercise() => new Exercise
    {
        ExerciseId = 995,
        Name = "GuessInversion"
    };

    [Fact]
    public void Generator_PicksOneOfThreeInversions_AndReturnsThreeNotes()
    {
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string>());

            var json = JObject.FromObject(raw);
            var inversion = (string?)json["inversion"];
            var notes = json["notes"] as JArray;

            inversion.Should().BeOneOf(AllowedInversions);
            notes.Should().NotBeNull();
            notes!.Count.Should().Be(3, "a triad has exactly three notes");
        }
    }

    [Fact]
    public void PlaybackPlanner_StacksAllChordNotesAtSameStartTime()
    {
        var planner = new ExercisePlaybackPlanner();
        var plan = planner.Plan(
            NewExercise(),
            new Dictionary<string, string> { { "invQuality", "major" }, { "noteRange", "C4-C4" } });

        plan.PlaybackPlans.Should().HaveCount(1);
        var chord = plan.PlaybackPlans[0];
        chord.Count.Should().Be(3);

        // All 3 notes must start simultaneously — that's what makes it a chord.
        var firstStart = chord[0].StartTimeSeconds;
        chord.All(c => Math.Abs(c.StartTimeSeconds - firstStart) < 0.0001)
             .Should().BeTrue("all chord notes must share the same start time");
    }
}
