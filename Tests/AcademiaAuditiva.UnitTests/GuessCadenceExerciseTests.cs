using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the <c>GuessCadence</c> exercise wiring. Verifies that
/// the generator only ever picks one of the four documented cadences,
/// always emits exactly four chord arrays (each with at least 2 notes),
/// and that the playback planner schedules every chord to start strictly
/// after the previous one — i.e. progressions play sequentially, never
/// stacked on top of each other.
/// </summary>
public class GuessCadenceExerciseTests
{
    private static readonly string[] AllowedCadences =
        { "perfect", "plagal", "imperfect", "deceptive" };

    private static Exercise NewExercise() => new Exercise
    {
        ExerciseId = 996,
        Name = "GuessCadence"
    };

    [Fact]
    public void Generator_PicksOneOfFourCadences_AndProducesFourChords()
    {
        var exercise = NewExercise();

        for (var i = 0; i < 200; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string>());

            var json = JObject.FromObject(raw);
            var cadence = (string?)json["cadence"];
            var chords = json["chords"] as JArray;

            cadence.Should().BeOneOf(AllowedCadences);
            chords.Should().NotBeNull();
            chords!.Count.Should().Be(4, "every cadence is a 4-chord progression");

            foreach (var chord in chords)
            {
                (chord as JArray).Should().NotBeNull();
                ((JArray)chord!).Count.Should().BeGreaterOrEqualTo(2,
                    "each chord must contain at least two notes");
            }
        }
    }

    [Fact]
    public void Generator_HonoursMinorScale_WhenFilterIsProvided()
    {
        var exercise = NewExercise();

        // Run several rolls — at least one should produce minor-flavored
        // chord notes for the I chord (root + minor-third interval).
        var sawMinorIChord = false;
        for (var i = 0; i < 20; i++)
        {
            var raw = MusicTheoryService.GenerateNoteForExercise(
                exercise,
                new Dictionary<string, string>
                {
                    { "cadenceRoot", "A" },
                    { "cadenceScale", "minor" }
                });

            var json = JObject.FromObject(raw);
            var firstChord = ((JArray)json["chords"]!)[0] as JArray;
            firstChord.Should().NotBeNull();
            // I in A minor = A-C-E, in octave 4 without a note range (where the slider starts)
            ((string?)firstChord![0]).Should().Be("A4");
            ((string?)firstChord![1]).Should().Be("C5");
            sawMinorIChord = true;
        }
        sawMinorIChord.Should().BeTrue();
    }

    [Fact]
    public void PlaybackPlanner_SchedulesChordsSequentially()
    {
        var planner = new ExercisePlaybackPlanner();
        var plan = planner.Plan(
            NewExercise(),
            new Dictionary<string, string>
            {
                { "cadenceRoot", "C" },
                { "cadenceScale", "major" }
            });

        plan.PlaybackPlans.Should().HaveCount(1);
        var sequence = plan.PlaybackPlans[0];
        sequence.Should().NotBeEmpty();

        // Group MixInputs by start time — that gives us the chords. We
        // expect 4 distinct start-time clusters, each strictly increasing.
        var startTimes = sequence.Select(s => s.StartTimeSeconds).Distinct().OrderBy(t => t).ToList();
        startTimes.Should().HaveCount(4, "a cadence is a 4-chord progression");

        for (var i = 1; i < startTimes.Count; i++)
        {
            startTimes[i].Should().BeGreaterThan(startTimes[i - 1],
                $"chord at index {i} must start after chord at index {i - 1}");
        }
    }
}
