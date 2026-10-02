using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every exercise is played on the instrument the student picked, with notes the
/// instrument has a sample for, in the octaves where it sounds natural.
/// </summary>
public class ExercisePlaybackPlannerInstrumentTests
{
    private static readonly HashSet<string> NoteFiles =
    [
        .. Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
            .Select(PianoSamples.BlobName),
    ];

    private readonly ExercisePlaybackPlanner _planner = new();

    public static TheoryData<string, string> SeededExercisesOnEveryInstrument
    {
        get
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"instruments-{Guid.NewGuid():N}")
                .Options);
            SeedData.SeedExercises(db);

            var data = new TheoryData<string, string>();
            foreach (var name in db.Exercises.Select(e => e.Name).OrderBy(n => n).ToList())
            {
                foreach (var instrument in Instrument.All)
                {
                    data.Add(name, instrument.Name);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SeededExercisesOnEveryInstrument))]
    public void EveryExercise_PlaysANoteSampleOfTheInstrument(string exerciseName, string instrumentName)
    {
        var instrument = Instrument.FromName(instrumentName);
        var folder = instrument.Folder is null ? "" : instrument.Folder + "/";
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        for (var round = 0; round < 20; round++)
        {
            // The widest range the sliders allow, so the planner has to keep the notes in.
            var filters = new Dictionary<string, string> { ["instrument"] = instrumentName, ["noteRange"] = "C1-C6" };

            foreach (var input in _planner.Plan(exercise, filters).PlaybackPlans.SelectMany(plan => plan))
            {
                input.SampleName.Should().StartWith(folder, "{0} is played on the {1}", exerciseName, instrumentName);
                NoteFiles.Should().Contain(input.SampleName[folder.Length..],
                    "{0} plays notes the {1} has a sample for", exerciseName, instrumentName);
            }
        }
    }

    [Fact]
    public void TheViolin_NeverPlaysBelowItsRange()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessNote" };

        for (var round = 0; round < 30; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Violin", ["noteRange"] = "C1-C2" });

            plan.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle()
                .Which.SampleName.Should().MatchRegex(@"^violin/[A-G]s?4\.mp3$");
            JObject.Parse(plan.ExpectedAnswerJson).Value<string>("note").Should().EndWith("4",
                "the answer is the note that is played");
        }
    }

    [Fact]
    public void HigherOrLower_OnTheGuitar_ComparesNotesUpToItsHighestOctave()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "HigherOrLower" };

        for (var round = 0; round < 30; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["noteRange"] = "C6-C6" });

            plan.PlaybackPlans.Should().ContainSingle().Which.Should().HaveCount(2)
                .And.AllSatisfy(input => input.SampleName.Should().MatchRegex(@"^guitar/[A-G]s?[45]\.mp3$"));
        }
    }

    [Fact]
    public void WithoutAnInstrument_ThePianoPlays()
    {
        var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = "GuessNote" }, []);

        plan.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle()
            .Which.SampleName.Should().MatchRegex(@"^[A-G]s?4\.mp3$");
    }

    [Fact]
    public void Plan_LeavesTheCallersFiltersAlone()
    {
        var filters = new Dictionary<string, string> { ["instrument"] = "Violin", ["noteRange"] = "C1-C2" };

        _planner.Plan(new Exercise { ExerciseId = 1, Name = "GuessNote" }, filters);

        filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["instrument"] = "Violin", ["noteRange"] = "C1-C2" });
    }
}
