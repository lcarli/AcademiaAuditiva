using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every exercise is played on the instrument the student picked, with notes the
/// instrument has a sample for, in the octaves where it sounds natural, and chords
/// only on the instruments that play them.
/// </summary>
public class ExercisePlaybackPlannerInstrumentTests
{
    private static readonly HashSet<string> NoteFiles =
    [
        .. Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
            .Select(PianoSamples.BlobName),
    ];

    // The MIDI note of each guitar sample.
    private static readonly Dictionary<string, int> GuitarNotes =
        Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
            .ToDictionary(Instrument.Guitar.SampleName);

    private readonly ExercisePlaybackPlanner _planner = new();

    private static List<string> SeededExerciseNames()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"instruments-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        return [.. db.Exercises.Select(e => e.Name).OrderBy(n => n)];
    }

    public static TheoryData<string> SeededExercises
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in SeededExerciseNames())
            {
                data.Add(name);
            }
            return data;
        }
    }

    public static TheoryData<string, string> SeededExercisesOnEveryInstrument
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in SeededExerciseNames())
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
        // Exercises that play chords play them on the piano when the instrument doesn't.
        var instrument = Instrument.FromName(instrumentName, ExercisePlaybackPlanner.PlaysChords(exerciseName));
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

    [Theory]
    [InlineData("GuessChords")]
    [InlineData("GuessFunction")]
    [InlineData("GuessQuality")]
    [InlineData("GuessInversion")]
    [InlineData("GuessCadence")]
    public void ChordExercises_OnTheViolin_ArePlayedOnThePiano(string exerciseName)
    {
        var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = exerciseName }, new() { ["instrument"] = "Violin" });

        ExercisePlaybackPlanner.PlaysChords(exerciseName).Should().BeTrue();
        plan.PlaybackPlans.Should().ContainSingle().Which.Should().NotBeEmpty()
            .And.AllSatisfy(input => input.SampleName.Should().MatchRegex(@"^[A-G]s?\d\.mp3$",
                "a violin plays one note at a time"));
    }

    [Theory]
    [InlineData("GuessChords")]
    [InlineData("GuessFunction")]
    [InlineData("GuessQuality")]
    [InlineData("GuessInversion")]
    public void Chords_OnTheGuitar_AreStrummedOnAShapeOfTheNeck(string exerciseName)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        for (var round = 0; round < 30; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["chordType"] = "all" });

            var chord = Midis(JObject.Parse(plan.ExpectedAnswerJson)["notes"]!);
            ShouldStrum(plan.PlaybackPlans.Should().ContainSingle().Subject, chord, startTime: 0.0, seconds: 1.5);
        }
    }

    [Fact]
    public void Cadences_OnTheGuitar_StrumEveryChordInTurn()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessCadence" };

        for (var round = 0; round < 20; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar" });

            var chords = JObject.Parse(plan.ExpectedAnswerJson)["chords"]!.Select(Midis).ToList();
            var strums = plan.PlaybackPlans.Should().ContainSingle().Subject
                .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                .ToList();
            strums.Select(strum => strum.Key).Should().Equal(0, 1, 2, 3);
            chords.Should().HaveCount(4);
            for (var k = 0; k < chords.Count; k++)
            {
                ShouldStrum([.. strums[k]], chords[k], startTime: k * 1.25, seconds: 1.2);
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeededExercises))]
    public void PlaysChords_ExactlyForTheExercisesThatPlayNotesTogether(string exerciseName)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        var notesTogether = Enumerable.Range(0, 10)
            .SelectMany(_ => _planner.Plan(exercise, new() { ["instrument"] = "Piano" }).PlaybackPlans)
            .Any(plan => plan.GroupBy(input => input.StartTimeSeconds).Any(notes => notes.Count() > 1));

        ExercisePlaybackPlanner.PlaysChords(exerciseName).Should().Be(notesTogether,
            "the exercises that play chords on the piano leave out the violin");
    }

    /// <summary>
    /// <paramref name="strum"/> strums <paramref name="chord"/> on the guitar from its bass up,
    /// one string every 15 ms from <paramref name="startTime"/>, every string ringing until
    /// <paramref name="seconds"/> after it.
    /// </summary>
    private static void ShouldStrum(IReadOnlyList<MixInput> strum, IReadOnlyList<int> chord, double startTime, double seconds)
    {
        strum.Should().AllSatisfy(input => GuitarNotes.Should().ContainKey(input.SampleName));
        var notes = strum.Select(input => GuitarNotes[input.SampleName]).ToList();

        notes.Should().HaveCountGreaterThanOrEqualTo(4, "a strum sounds four to six strings")
            .And.BeInAscendingOrder("a downstroke strums from the low string up")
            .And.OnlyHaveUniqueItems();
        notes.Select(PitchClass).Distinct().Should().BeEquivalentTo(chord.Select(PitchClass).Distinct(),
            "the strum plays every note of the chord, and only them");
        PitchClass(notes[0]).Should().Be(PitchClass(chord.Min()), "an inversion stays one");
        for (var i = 0; i < strum.Count; i++)
        {
            strum[i].StartTimeSeconds.Should().BeApproximately(startTime + i * 0.015, 1e-9);
            (strum[i].StartTimeSeconds + strum[i].DurationSeconds).Should().BeApproximately(startTime + seconds, 1e-9,
                "every string rings until the chord ends");
        }
    }

    private static List<int> Midis(JToken notes) =>
        [.. notes.Values<string>().Select(note => MusicTheoryService.NoteToMidi(note!) ?? throw new ArgumentException(note))];

    private static int PitchClass(int midi) => midi % 12;
}
