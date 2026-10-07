using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.ExerciseValidators;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessProgression plays a chord progression after the cadence of its key, I–IV–V–I in major
/// and i–iv–V–i in minor, as GuessFunction and GuessDegree do. The student names the
/// progression or, in dictation, gives the degrees of chords 2 to 4 of one that starts on the
/// tonic and moves as tonal harmony does. A minor key's chords are those of the natural minor,
/// VII a whole step below the tonic, but its V keeps the leading tone.
/// </summary>
public class GuessProgressionExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessProgression" };

    // The chord of each Roman numeral of a key: the dictation's answer code (its degree and
    // quality), and its root in semitones above the tonic.
    private static readonly Dictionary<string, (string Code, int Root)> MajorChords = new()
    {
        ["I"] = ("1-major", 0),
        ["ii"] = ("2-minor", 2),
        ["iii"] = ("3-minor", 4),
        ["IV"] = ("4-major", 5),
        ["V"] = ("5-major", 7),
        ["vi"] = ("6-minor", 9),
        ["vii°"] = ("7-diminished", 11),
    };

    private static readonly Dictionary<string, (string Code, int Root)> MinorChords = new()
    {
        ["i"] = ("1-minor", 0),
        ["ii°"] = ("2-diminished", 2),
        ["III"] = ("3-major", 3),
        ["iv"] = ("4-minor", 5),
        ["V"] = ("5-major", 7),
        ["VI"] = ("6-major", 8),
        ["VII"] = ("7-major", 10),
    };

    private static readonly Dictionary<string, int[]> Qualities = new()
    {
        ["major"] = [0, 4, 7],
        ["minor"] = [0, 3, 7],
        ["diminished"] = [0, 3, 6],
    };

    // Where the dictation may go from each chord: toward the dominant and back to the tonic,
    // or down by fifths.
    private static readonly Dictionary<string, string> MajorMoves = new()
    {
        ["I"] = "ii iii IV V vi",
        ["ii"] = "V vii°",
        ["iii"] = "vi IV",
        ["IV"] = "I ii V vii°",
        ["V"] = "I vi",
        ["vi"] = "ii IV",
        ["vii°"] = "I",
    };

    private static readonly Dictionary<string, string> MinorMoves = new()
    {
        ["i"] = "ii° III iv V VI VII",
        ["ii°"] = "V",
        ["III"] = "VI iv",
        ["iv"] = "i V VII",
        ["V"] = "i VI",
        ["VI"] = "ii° III iv VII",
        ["VII"] = "III i",
    };

    [Theory]
    [InlineData("C", "major", "C4 E4 G4", "F4 A4 C5", "G4 B4 D5")]
    [InlineData("A", "minor", "A4 C5 E5", "D5 F5 A5", "E5 G#5 B5")]
    [InlineData("F#", "minor", "F#4 A4 C#5", "B4 D5 F#5", "C#5 E#5 G#5")]
    public void EveryRound_StartsWithTheCadenceOfItsKey(string key, string scale, string tonic, string subdominant, string dominant)
    {
        foreach (var level in new[] { "name", "numerals" })
        {
            for (var i = 0; i < 20; i++)
            {
                var cadence = Round(key, scale, level)["cadence"]!.Select(Midis).ToList();

                cadence.Should().HaveCount(4);
                cadence[0].Should().Equal(MidisOf(tonic));
                cadence[1].Should().Equal(MidisOf(subdominant));
                cadence[2].Should().Equal(MidisOf(dominant), "the dominant has the leading tone, in minor too");
                cadence[3].Should().Equal(MidisOf(tonic), "the cadence comes back to the tonic");
            }
        }
    }

    [Theory]
    [InlineData("C", "major", "I-V-vi-IV", "C4 E4 G4|G4 B4 D5|A4 C5 E5|F4 A4 C5")]
    [InlineData("C", "major", "I-vi-IV-V", "C4 E4 G4|A4 C5 E5|F4 A4 C5|G4 B4 D5")]
    [InlineData("C", "major", "ii-V-I", "D4 F4 A4|G4 B4 D5|C4 E4 G4")]
    // The Andalusian cadence: Am G F E, with the natural G and the major E.
    [InlineData("A", "minor", "i-VII-VI-V", "A4 C5 E5|G5 B5 D6|F5 A5 C6|E5 G#5 B5")]
    [InlineData("A", "minor", "i-VI-III-VII", "A4 C5 E5|F5 A5 C6|C5 E5 G5|G5 B5 D6")]
    [InlineData("A", "minor", "iio-V-i", "B4 D5 F5|E5 G#5 B5|A4 C5 E5")]
    public void EachProgression_IsItsChordsInTheKey(string key, string scale, string progression, string chords)
    {
        var rounds = Enumerable.Range(0, 200).Select(_ => Round(key, scale, "name"))
            .Where(round => round.Value<string>("answer") == progression)
            .ToList();

        rounds.Should().NotBeEmpty("{0} is asked in {1} {2}", progression, key, scale);
        rounds.Should().AllSatisfy(round => round["chords"]!.Select(Midis).Should().BeEquivalentTo(
            chords.Split('|').Select(MidisOf), options => options.WithStrictOrdering()));
    }

    [Fact]
    public void TheBlues_IsTwelveBarsOfDominantSevenths()
    {
        var (i7, iv7, v7) = (MidisOf("G4 B4 D5 F5"), MidisOf("C5 E5 G5 Bb5"), MidisOf("D5 F#5 A5 C6"));

        var rounds = Enumerable.Range(0, 200).Select(_ => Round("G", "major", "name"))
            .Where(round => round.Value<string>("answer") == "blues")
            .ToList();

        rounds.Should().NotBeEmpty();
        rounds.Should().AllSatisfy(round => round["chords"]!.Select(Midis).Should().BeEquivalentTo(
            new[] { i7, i7, i7, i7, iv7, iv7, i7, i7, v7, iv7, i7, i7 }, options => options.WithStrictOrdering()));
    }

    [Theory]
    [InlineData("major")]
    [InlineData("minor")]
    public void TheDictation_StartsOnTheTonic_ThenMovesAsTonalHarmonyDoes(string scale)
    {
        var (chords, moves, tonicNumeral) = scale == "minor" ? (MinorChords, MinorMoves, "i") : (MajorChords, MajorMoves, "I");
        var numerals = chords.ToDictionary(chord => chord.Value.Code, chord => chord.Key);
        var asked = new HashSet<string>();
        var movesMade = new HashSet<(string From, string To)>();

        for (var i = 0; i < 600; i++)
        {
            var round = Round("any", scale, "numerals", "C2-C5");

            var codes = round.Value<string>("answer")!.Split('|');
            codes.Should().HaveCount(3, "the student gives chords 2 to 4").And.OnlyContain(code => numerals.ContainsKey(code));
            List<string> progression = [tonicNumeral, .. codes.Select(code => numerals[code])];
            var tonic = Midis(round["cadence"]![0]!)[0];
            round["chords"]!.Select(Midis).Should().BeEquivalentTo(
                progression.Select(numeral => ChordOf(tonic, chords[numeral])), options => options.WithStrictOrdering(),
                "{0} is played in the key of the cadence", string.Join(' ', progression));
            for (var k = 1; k < progression.Count; k++)
            {
                moves[progression[k - 1]].Split(' ').Should().Contain(progression[k], "{0} may follow {1}", progression[k], progression[k - 1]);
                movesMade.Add((progression[k - 1], progression[k]));
            }
            asked.UnionWith(codes);
        }

        asked.Should().BeEquivalentTo(chords.Values.Select(chord => chord.Code), "every chord of the key is asked");
        movesMade.Should().BeEquivalentTo(moves.SelectMany(move => move.Value.Split(' ').Select(to => (move.Key, to))),
            "every move is made");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("H", "dorian", "expert")]
    [InlineData("Bxxxx", "major", "")]
    public void AnUnknownKeyScaleOrLevel_IsCMajor_ByName(string? key, string? scale, string? level)
    {
        var rounds = Enumerable.Range(0, 200).Select(_ => Round(key, scale, level)).ToList();

        rounds.Should().AllSatisfy(round => round["cadence"]!.Select(Midis).Should().BeEquivalentTo(
            new[] { MidisOf("C4 E4 G4"), MidisOf("F4 A4 C5"), MidisOf("G4 B4 D5"), MidisOf("C4 E4 G4") },
            options => options.WithStrictOrdering()));
        rounds.Select(round => round.Value<string>("answer")).Distinct()
            .Should().BeEquivalentTo(new[] { "I-V-vi-IV", "I-vi-IV-V", "ii-V-I", "blues" });
    }

    [Fact]
    public void AnyKey_DrawsTheKeyOfEachQuestion()
    {
        var tonics = Enumerable.Range(0, 300)
            .Select(_ => PitchClass(Midi((string)Round("any", "major", "name")["cadence"]![0]![0]!)))
            .ToHashSet();

        tonics.Should().HaveCount(12, "every key of the filter can be drawn");
    }

    [Fact]
    public void EveryProgression_HasAButton_AndEveryButtonIsAProgression()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-progression-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessProgression").AnswerButtonsJson!)!["guessAnswer"];

        var answers = new[] { "major", "minor" }
            .SelectMany(scale => Enumerable.Range(0, 200).Select(_ => Round("E", scale, "name").Value<string>("answer")!))
            .ToHashSet();

        buttons.Values.Should().BeEquivalentTo(answers, "the student can give every answer, and every button can be right");
        answers.Select(answer => answer.ToUpperInvariant()).Should().OnlyHaveUniqueItems("answers are checked ignoring case");
    }

    [Fact]
    public void TheDictation_IsRightOnlyWhenEveryChordIs()
    {
        var validator = new GuessProgressionValidator();
        var expected = JsonConvert.SerializeObject(new { answer = "6-minor|2-minor|5-major" });

        validator.Validate("6-minor|2-minor|5-major", expected).IsCorrect.Should().BeTrue();
        validator.Validate("6-minor|2-minor|4-major", expected).IsCorrect.Should().BeFalse("the last chord is wrong");
        validator.Validate("6-minor|5-major|2-minor", expected).IsCorrect.Should().BeFalse("the chords are out of order");
        validator.Validate("6-minor|2-minor", expected).IsCorrect.Should().BeFalse("a chord is missing");
    }

    // The range sets the octave of the key, and the progression follows it: an octave lower when a
    // note of either would go past the highest sample.
    [Theory]
    [InlineData("C1-C1", new[] { 1 })]
    [InlineData("C6-C6", new[] { 5, 6 })]
    public void EveryNote_HasASample_InTheOctaveOfTheRange(string noteRange, int[] tonicOctaves)
    {
        foreach (var scale in new[] { "major", "minor" })
        {
            foreach (var level in new[] { "name", "numerals" })
            {
                for (var i = 0; i < 100; i++)
                {
                    var round = Round("any", scale, level, noteRange);

                    round["cadence"]!.Concat(round["chords"]!).SelectMany(Midis).Should().OnlyContain(midi => PianoSamples.Covers(midi));
                    (Midis(round["cadence"]![0]!)[0] / 12 - 1).Should().BeOneOf(tonicOctaves);
                }
            }
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void ThePlanner_PlaysTheCadence_ASilentBeat_ThenTheProgression_OnThePiano(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            foreach (var level in new[] { "name", "numerals" })
            {
                for (var i = 0; i < 10; i++)
                {
                    var plan = planner.Plan(Exercise, new()
                    {
                        ["instrument"] = instrumentName, ["keySelect"] = "D", ["scaleTypeSelect"] = scale, ["gpLevel"] = level,
                    });

                    // At GuessCadence's pace, a chord every 1.25 s; the violin leaves the chords to the piano.
                    var round = JObject.Parse(plan.ExpectedAnswerJson);
                    plan.PlaybackPlans.Should().ContainSingle("Replay plays the key again before the progression")
                        .Which.Should().Equal(OnThePiano(round["cadence"]!, 0.0).Concat(OnThePiano(round["chords"]!, 6.25)));
                }
            }
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Guitar")]
    public void TheBlues_FitsInOneMix(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();

        var blues = Enumerable.Range(0, 200)
            .Select(_ => planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["keySelect"] = "any" }))
            .Where(plan => JObject.Parse(plan.ExpectedAnswerJson).Value<string>("answer") == "blues")
            .ToList();

        // Its twelfth bar starts at 20 s: the mix lasts 21.2 s, under the 30 s a mix may last.
        blues.Should().NotBeEmpty().And.AllSatisfy(plan => plan.PlaybackPlans.Should().ContainSingle()
            .Which.Max(input => input.StartTimeSeconds + input.DurationSeconds).Should().BeApproximately(21.2, 1e-9));
    }

    [Theory]
    [InlineData(null, GuitarPosition.Open)]
    [InlineData("Barre", GuitarPosition.Barre)]
    [InlineData("High", GuitarPosition.High)]
    public void OnTheGuitar_TheKeyAndTheProgression_AreStrummedWhereTheStudentPicked(string? guitarPosition, GuitarPosition position)
    {
        var planner = new ExercisePlaybackPlanner();

        foreach (var scale in new[] { "major", "minor" })
        {
            foreach (var level in new[] { "name", "numerals" })
            {
                var filters = new Dictionary<string, string>
                {
                    ["instrument"] = "Guitar", ["keySelect"] = "any", ["scaleTypeSelect"] = scale, ["gpLevel"] = level,
                };
                if (guitarPosition is not null)
                    filters["guitarPosition"] = guitarPosition;

                for (var i = 0; i < 20; i++)
                {
                    var plan = planner.Plan(Exercise, filters);

                    var round = JObject.Parse(plan.ExpectedAnswerJson);
                    var chords = round["cadence"]!.Concat(round["chords"]!).Select(Midis).ToList();
                    var strums = plan.PlaybackPlans.Should().ContainSingle().Subject
                        .GroupBy(input => (int)Math.Floor(input.StartTimeSeconds / 1.25 + 1e-9))
                        .ToList();
                    strums.Select(strum => strum.Key).Should().Equal(
                        [0, 1, 2, 3, .. Enumerable.Range(5, chords.Count - 4)], "a silent beat comes between the key and the progression");
                    for (var k = 0; k < chords.Count; k++)
                    {
                        strums[k].Select(input => input.SampleName).Should().Equal(
                            GuitarVoicing.Find(chords[k], position)!.Notes.Select(Instrument.Guitar.SampleName));
                    }
                }
            }
        }
    }

    private static JObject Round(string? key, string? scale, string? level, string? noteRange = null)
    {
        var filters = new Dictionary<string, string>();
        if (key is not null)
            filters["keySelect"] = key;
        if (scale is not null)
            filters["scaleTypeSelect"] = scale;
        if (level is not null)
            filters["gpLevel"] = level;
        if (noteRange is not null)
            filters["noteRange"] = noteRange;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    /// <summary>The chord <paramref name="chord"/> of the key of <paramref name="tonic"/>, in root position.</summary>
    private static List<int> ChordOf(int tonic, (string Code, int Root) chord) =>
        [.. Qualities[chord.Code.Split('-')[1]].Select(semitones => tonic + chord.Root + semitones)];

    /// <summary>Each chord of <paramref name="chords"/> on the piano, one every 1.25 s from <paramref name="start"/>.</summary>
    private static IEnumerable<MixInput> OnThePiano(JToken chords, double start) =>
        chords.SelectMany((chord, k) => chord.Values<string>()
            .Select(note => new MixInput(Instrument.Piano.SampleFor(note!), start + k * 1.25, 1.2)));

    private static List<int> Midis(JToken chord) => [.. chord.Values<string>().Select(note => Midi(note!))];

    private static List<int> MidisOf(string notes) => [.. notes.Split(' ').Select(Midi)];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    private static int PitchClass(int semitones) => ((semitones % 12) + 12) % 12;
}
