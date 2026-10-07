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

    // The exercises that set their key with a cadence before the question, and when the question
    // starts: after the four chords of the cadence and a silent beat, 1.25 s each.
    private static readonly Dictionary<string, double> QuestionStart = new()
    {
        ["GuessFunction"] = 6.25,
        ["GuessDegree"] = 6.25,
    };

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
        // Exercises about chords play them on the piano when the instrument doesn't, and so does
        // the cadence that sets the key before a question.
        var instrument = Instrument.FromName(instrumentName, ExercisePlaybackPlanner.IsChordExercise(exerciseName));
        var accompaniment = instrument.PlaysChords ? instrument : Instrument.Piano;
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        for (var round = 0; round < 20; round++)
        {
            // The widest range the sliders allow, so the planner has to keep the notes in.
            var filters = new Dictionary<string, string> { ["instrument"] = instrumentName, ["noteRange"] = "C1-C6" };

            foreach (var input in _planner.Plan(exercise, filters).PlaybackPlans.SelectMany(plan => plan))
            {
                var playedOn = input.StartTimeSeconds < QuestionStart.GetValueOrDefault(exerciseName) ? accompaniment : instrument;
                var folder = playedOn.Folder is null ? "" : playedOn.Folder + "/";
                input.SampleName.Should().StartWith(folder, "{0} is played on the {1}", exerciseName, playedOn.Name);
                NoteFiles.Should().Contain(input.SampleName[folder.Length..],
                    "{0} plays notes the {1} has a sample for", exerciseName, playedOn.Name);
            }
        }
    }

    [Fact]
    public void TheViolin_NeverPlaysBelowItsGString()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessNote" };

        for (var round = 0; round < 30; round++)
        {
            // Octaves 1 and 2 are below the violin: it plays its lowest one, from its G string up.
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Violin", ["noteRange"] = "C1-C2" });

            plan.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle()
                .Which.SampleName.Should().MatchRegex(@"^violin/(G|Gs|A|As|B)3\.mp3$");
            JObject.Parse(plan.ExpectedAnswerJson).Value<string>("note").Should().BeOneOf(
                new[] { "G3", "G#3", "A3", "A#3", "B3" }, "the answer is the note that is played");
        }
    }

    [Fact]
    public void GuessNote_OnTheGuitar_PlaysOnlyTheNotesOfItsNeck()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessNote" };

        for (var round = 0; round < 30; round++)
        {
            // The neck has no C2 to D#2: its lowest note is the low E string.
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["noteRange"] = "C2-C2" });

            var note = JObject.Parse(plan.ExpectedAnswerJson).Value<string>("note")!;
            Midi(note).Should().BeInRange(Midi("E2"), Midi("B2"));
            plan.PlaybackPlans.Should().ContainSingle().Which.Should().ContainSingle()
                .Which.SampleName.Should().Be(Instrument.Guitar.SampleFor(note), "the answer is the note that is played");
        }
    }

    [Theory]
    [InlineData("C1-C2", "E2", "B3")]
    [InlineData("C6-C6", "C4", "B5")]
    public void HigherOrLower_OnTheGuitar_ComparesNotesOfItsNeck(string noteRange, string lowest, string highest)
    {
        // One octave is widened to the next one up, or down from the highest: the neck goes from E2 to B5.
        var exercise = new Exercise { ExerciseId = 1, Name = "HigherOrLower" };

        for (var round = 0; round < 30; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["noteRange"] = noteRange });

            plan.PlaybackPlans.Should().ContainSingle().Which.Should().HaveCount(2)
                .And.AllSatisfy(input => GuitarNotes[input.SampleName].Should().BeInRange(Midi(lowest), Midi(highest)));
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
    [InlineData("CompleteChord")]
    public void ChordExercises_OnTheViolin_ArePlayedOnThePiano(string exerciseName)
    {
        var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = exerciseName }, new() { ["instrument"] = "Violin" });

        ExercisePlaybackPlanner.IsChordExercise(exerciseName).Should().BeTrue();
        plan.PlaybackPlans.Should().ContainSingle().Which.Should().NotBeEmpty()
            .And.AllSatisfy(input => input.SampleName.Should().MatchRegex(@"^[A-G]s?\d\.mp3$",
                "the exercises about chords leave out the violin, which plays one note at a time"));
    }

    [Theory]
    [InlineData("GuessChords")]
    [InlineData("GuessQuality")]
    [InlineData("GuessInversion")]
    public void Chords_OnTheGuitar_AreStrummedOnAShapeOfTheNeck(string exerciseName)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        foreach (var position in Enum.GetNames<GuitarPosition>())
        {
            for (var round = 0; round < 15; round++)
            {
                var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["chordType"] = "all", ["guitarPosition"] = position });

                var chord = Midis(JObject.Parse(plan.ExpectedAnswerJson)["notes"]!);
                ShouldStrum(plan.PlaybackPlans.Should().ContainSingle().Subject, chord, startTime: 0.0, seconds: 1.5);
            }
        }
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Barre")]
    [InlineData("High")]
    public void Cadences_OnTheGuitar_StrumEveryChordInTurn(string position)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessCadence" };

        for (var round = 0; round < 20; round++)
        {
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["guitarPosition"] = position });

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
    [InlineData("Open")]
    [InlineData("Barre")]
    [InlineData("High")]
    public void Functions_OnTheGuitar_StrumTheKeyThenTheChord(string position)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessFunction" };

        foreach (var scale in new[] { "major", "minor" })
        {
            for (var round = 0; round < 15; round++)
            {
                var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["guitarPosition"] = position, ["scaleTypeSelect"] = scale });

                var answer = JObject.Parse(plan.ExpectedAnswerJson);
                var cadence = answer["cadence"]!.Select(Midis).ToList();
                var strums = plan.PlaybackPlans.Should().ContainSingle().Subject
                    .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                    .ToList();
                strums.Select(strum => strum.Key).Should().Equal([0, 1, 2, 3, 5], "a silent beat comes between the key and the chord");
                cadence.Should().HaveCount(4);
                for (var k = 0; k < cadence.Count; k++)
                {
                    ShouldStrum([.. strums[k]], cadence[k], startTime: k * 1.25, seconds: 1.2);
                }
                ShouldStrum([.. strums[4]], Midis(answer["notes"]!), startTime: 6.25, seconds: 1.5);
            }
        }
    }

    [Fact]
    public void Cadences_OnTheGuitar_StartOnTheOpenChords()
    {
        // In A major: A, D, E and F#m, as a guitarist plays them in the first frets.
        var openChords = new Dictionary<int, string>
        {
            [PitchClass(Midi("A2"))] = "x02220",
            [PitchClass(Midi("D3"))] = "xx0232",
            [PitchClass(Midi("E2"))] = "022100",
            [PitchClass(Midi("F#2"))] = "244222",
        };
        var exercise = new Exercise { ExerciseId = 1, Name = "GuessCadence" };

        for (var round = 0; round < 20; round++)
        {
            // Without a position, as when the student hasn't picked one.
            var plan = _planner.Plan(exercise, new() { ["instrument"] = "Guitar", ["cadenceRoot"] = "A" });

            var chords = JObject.Parse(plan.ExpectedAnswerJson)["chords"]!.Select(Midis).ToList();
            var strums = plan.PlaybackPlans.Should().ContainSingle().Subject
                .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                .ToList();
            strums.Should().HaveSameCount(chords);
            for (var k = 0; k < chords.Count; k++)
            {
                strums[k].Select(input => GuitarNotes[input.SampleName]).Should()
                    .Equal(ShapeNotes(openChords[PitchClass(chords[k][0])]));
            }
        }
    }

    [Theory]
    [InlineData("GuessChords", null, GuitarPosition.Open)]
    [InlineData("GuessChords", "Open", GuitarPosition.Open)]
    [InlineData("GuessChords", "Barre", GuitarPosition.Barre)]
    [InlineData("GuessChords", "high", GuitarPosition.High)]
    [InlineData("GuessChords", "Drums", GuitarPosition.Open)]
    [InlineData("GuessFunction", "Barre", GuitarPosition.Barre)]
    [InlineData("GuessFunction", "High", GuitarPosition.High)]
    [InlineData("GuessQuality", "Barre", GuitarPosition.Barre)]
    [InlineData("GuessQuality", "High", GuitarPosition.High)]
    [InlineData("GuessInversion", "Barre", GuitarPosition.Barre)]
    [InlineData("GuessInversion", "High", GuitarPosition.High)]
    [InlineData("GuessCadence", null, GuitarPosition.Open)]
    [InlineData("GuessCadence", "Barre", GuitarPosition.Barre)]
    [InlineData("GuessCadence", "High", GuitarPosition.High)]
    public void Chords_OnTheGuitar_ArePlayedWhereOnTheNeckTheStudentPicked(
        string exerciseName, string? guitarPosition, GuitarPosition position)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        // The shape sets the octaves of the strings, whatever the note range.
        foreach (var noteRange in new[] { "C1-C1", "C6-C6" })
        {
            var filters = new Dictionary<string, string> { ["instrument"] = "Guitar", ["chordType"] = "all", ["noteRange"] = noteRange };
            if (guitarPosition is not null)
                filters["guitarPosition"] = guitarPosition;

            for (var round = 0; round < 15; round++)
            {
                var plan = _planner.Plan(exercise, filters);

                var answer = JObject.Parse(plan.ExpectedAnswerJson);
                List<List<int>> chords = exerciseName switch
                {
                    "GuessCadence" => [.. answer["chords"]!.Select(Midis)],
                    // The cadence that sets the key, then the chord.
                    "GuessFunction" => [.. answer["cadence"]!.Select(Midis), Midis(answer["notes"]!)],
                    _ => [Midis(answer["notes"]!)],
                };
                var strums = plan.PlaybackPlans.Should().ContainSingle().Subject
                    .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                    .Select(strum => strum.Select(input => GuitarNotes[input.SampleName]).ToList())
                    .ToList();
                strums.Should().HaveSameCount(chords);
                for (var k = 0; k < chords.Count; k++)
                {
                    strums[k].Should().Equal(GuitarVoicing.Find(chords[k], position)!.Notes,
                        "{0} is strummed on its {1} shape with the note range {2}", exerciseName, position, noteRange);
                }
            }
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void CompleteChord_PlaysTheChordAsWritten_AllItsNotesTogether(string instrumentName)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "CompleteChord" };

        foreach (var octave in new[] { "3", "4" })
        {
            for (var round = 0; round < 20; round++)
            {
                var plan = _planner.Plan(exercise, new()
                {
                    ["instrument"] = instrumentName, ["ccQuality"] = "all", ["ccAccidentals"] = "any", ["ccOctave"] = octave,
                });

                var chord = JObject.Parse(plan.ExpectedAnswerJson)["chordNotes"]!.Values<string>();
                plan.PlaybackPlans.Should().ContainSingle().Which.Should().Equal(
                    chord.Select(note => new MixInput(Instrument.Piano.SampleFor(note!), 0.0, 2.0)),
                    "the chord rings for a whole note, on the piano when the instrument plays one note at a time");
            }
        }
    }

    [Fact]
    public void CompleteChord_OnTheGuitar_StrumsTheChordAsWritten()
    {
        var exercise = new Exercise { ExerciseId = 1, Name = "CompleteChord" };

        foreach (var octave in new[] { "3", "4" })
        {
            for (var round = 0; round < 20; round++)
            {
                // The position of the other chord exercises doesn't move it to a shape of the neck.
                var plan = _planner.Plan(exercise, new()
                {
                    ["instrument"] = "Guitar", ["guitarPosition"] = "High",
                    ["ccQuality"] = "all", ["ccAccidentals"] = "any", ["ccOctave"] = octave,
                });

                var chord = Midis(JObject.Parse(plan.ExpectedAnswerJson)["chordNotes"]!);
                var strum = plan.PlaybackPlans.Should().ContainSingle().Subject;
                strum.Select(input => GuitarNotes[input.SampleName]).Should().Equal(chord,
                    "the student hears the notes they write, from the lowest up");
                for (var i = 0; i < strum.Count; i++)
                {
                    strum[i].StartTimeSeconds.Should().BeApproximately(i * 0.015, 1e-9);
                    (strum[i].StartTimeSeconds + strum[i].DurationSeconds).Should().BeApproximately(2.0, 1e-9,
                        "every string rings until the whole note ends");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeededExercises))]
    public void IsChordExercise_ForEveryExerciseThatPlaysNotesTogether(string exerciseName)
    {
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        // Only the question counts: the cadence that sets a key before it is played on the piano
        // when the instrument plays no chords (GuessDegree's note on the violin).
        var notesTogether = Enumerable.Range(0, 10)
            .SelectMany(_ => _planner.Plan(exercise, new() { ["instrument"] = "Piano" }).PlaybackPlans)
            .Select(plan => plan.Where(input => input.StartTimeSeconds >= QuestionStart.GetValueOrDefault(exerciseName)))
            .Any(question => question.GroupBy(input => input.StartTimeSeconds).Any(notes => notes.Count() > 1));

        // CompleteChord plays its chord as written, not on a shape of the guitar's neck.
        ExercisePlaybackPlanner.IsChordExercise(exerciseName).Should().Be(notesTogether,
            "the exercises about chords leave out the violin");
        ExercisePlaybackPlanner.PlaysChords(exerciseName).Should().Be(notesTogether && exerciseName != "CompleteChord",
            "on the guitar the student picks where on the neck to play the chords");
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

    private static List<int> Midis(JToken notes) => [.. notes.Values<string>().Select(note => Midi(note!))];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    /// <summary>The MIDI notes a chord chart (<c>x02220</c>) strums, from the low string up.</summary>
    private static IReadOnlyList<int> ShapeNotes(string chart) =>
        new GuitarShape([.. chart.Select(fret => fret == 'x' ? (int?)null : fret - '0')]).Notes;

    private static int PitchClass(int midi) => midi % 12;
}
