using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The guitar plays a chord the way a guitarist does: a shape of the neck that one hand can
/// hold, strumming four to six strings from the chord's bass up, where on the neck the student
/// picks (<see cref="GuitarPosition"/>): the open chords, barre chords, or high on the neck.
/// </summary>
public class GuitarVoicingTests
{
    // The basses of the open chords: from the low E string (E2) up to D#3, as low as the neck has them.
    private const int LowestBass = 40;
    private const int HighestOpenChordBass = 51;

    private const int HighPositionFret = 7;

    private static readonly string[] Roots = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // Semitones from the root to each note of the triads and seventh chords the exercises play
    // (GuessChords, GuessFunction, GuessQuality, GuessInversion, GuessCadence and GuessProgression).
    private static readonly Dictionary<string, int[]> Qualities = new()
    {
        ["major"] = [0, 4, 7],
        ["minor"] = [0, 3, 7],
        ["diminished"] = [0, 3, 6],
        ["augmented"] = [0, 4, 8],
        ["major7"] = [0, 4, 7, 11],
        ["minor7"] = [0, 3, 7, 10],
        ["dominant7"] = [0, 4, 7, 10],
        ["halfDiminished"] = [0, 3, 6, 10],
        ["diminished7"] = [0, 3, 6, 9],
    };

    // The chords GuessQuality plays in root position only: the sus, 6 and add9 chords.
    private static readonly Dictionary<string, int[]> SusAndAddedChords = new()
    {
        ["sus2"] = [0, 2, 7],
        ["sus4"] = [0, 5, 7],
        ["major6"] = [0, 4, 7, 9],
        ["add9"] = [0, 4, 7, 14],
    };

    [Theory]
    // Open chords and barre chords, whose basses are in octave 2 (or octave 3: there is no C2 to D#2).
    [InlineData("C", "major", 0, "x32010")]
    [InlineData("C#", "major", 0, "x46664")]
    [InlineData("D", "major", 0, "xx0232")]
    [InlineData("D#", "major", 0, "x68886")]
    [InlineData("E", "major", 0, "022100")]
    [InlineData("F", "major", 0, "133211")]
    [InlineData("F#", "major", 0, "244322")]
    [InlineData("G", "major", 0, "320003")]
    [InlineData("G#", "major", 0, "466544")]
    [InlineData("A", "major", 0, "x02220")]
    [InlineData("A#", "major", 0, "x13331")]
    [InlineData("B", "major", 0, "x24442")]
    [InlineData("C", "minor", 0, "x35543")]
    [InlineData("C#", "minor", 0, "x46654")]
    [InlineData("D", "minor", 0, "xx0231")]
    [InlineData("D#", "minor", 0, "x68876")]
    [InlineData("E", "minor", 0, "022000")]
    [InlineData("F", "minor", 0, "133111")]
    [InlineData("F#", "minor", 0, "244222")]
    [InlineData("G", "minor", 0, "355333")]
    [InlineData("G#", "minor", 0, "466444")]
    [InlineData("A", "minor", 0, "x02210")]
    [InlineData("A#", "minor", 0, "x13321")]
    [InlineData("B", "minor", 0, "x24432")]
    // Inversions keep their bass: C/E, C/G, D/F#, D/A, G/B, Am/C, Am/E, Em/G, Dm/F…
    [InlineData("C", "major", 1, "032010")]
    [InlineData("C", "major", 2, "335553")]
    [InlineData("D", "major", 1, "200232")]
    [InlineData("D", "major", 2, "x00232")]
    [InlineData("E", "major", 2, "x22100")]
    [InlineData("F", "major", 1, "x03211")]
    [InlineData("F", "major", 2, "x33211")]
    [InlineData("G", "major", 1, "x20003")]
    [InlineData("G", "major", 2, "xx0003")]
    [InlineData("A", "major", 1, "x42220")]
    [InlineData("A", "major", 2, "002220")]
    [InlineData("A#", "major", 1, "xx0331")]
    [InlineData("A", "minor", 1, "x32210")]
    [InlineData("A", "minor", 2, "002210")]
    [InlineData("E", "minor", 1, "322000")]
    [InlineData("E", "minor", 2, "x22000")]
    [InlineData("D", "minor", 1, "100231")]
    [InlineData("D", "minor", 2, "x00231")]
    [InlineData("C#", "minor", 1, "042120")]
    [InlineData("B", "minor", 1, "xx0432")]
    [InlineData("G", "minor", 2, "xx0333")]
    // Augmented, diminished and seventh chords.
    [InlineData("C", "augmented", 0, "x32110")]
    [InlineData("E", "augmented", 0, "032110")]
    [InlineData("G", "augmented", 0, "321003")]
    [InlineData("D", "diminished", 0, "xx0131")]
    [InlineData("A", "diminished", 0, "x0121x")]
    [InlineData("C", "diminished", 0, "x3454x")]
    [InlineData("C", "major7", 0, "x32000")]
    [InlineData("D", "major7", 0, "xx0222")]
    [InlineData("E", "major7", 0, "021100")]
    [InlineData("G", "major7", 0, "320002")]
    [InlineData("A", "major7", 0, "x02120")]
    [InlineData("A", "minor7", 0, "x02010")]
    [InlineData("E", "minor7", 0, "020000")]
    [InlineData("D", "minor7", 0, "xx0211")]
    [InlineData("F", "minor7", 0, "131111")]
    [InlineData("D", "diminished7", 0, "xx0101")]
    [InlineData("E", "diminished7", 0, "012020")]
    [InlineData("D", "dominant7", 0, "xx0212")]
    [InlineData("E", "dominant7", 0, "020100")]
    [InlineData("F", "dominant7", 0, "131211")]
    [InlineData("G", "dominant7", 0, "320001")]
    [InlineData("A", "dominant7", 0, "x02020")]
    public void Find_OnTheOpenChords_PlaysTheShapeGuitaristsPlay(string root, string quality, int inversion, string shape)
    {
        var chord = Chord(root, quality, inversion);

        (GuitarVoicing.Find(chord, GuitarPosition.Open)?.ToString()).Should().Be(shape);
        (GuitarVoicing.Find(chord)?.ToString()).Should().Be(shape, "the guitar plays the open chords unless the student picks another position");
    }

    [Theory]
    // The E-shape and A-shape barre chords, as near the nut as the chord allows.
    [InlineData("C", "major", 0, "x35553")]
    [InlineData("C#", "major", 0, "x46664")]
    [InlineData("D", "major", 0, "x57775")]
    [InlineData("D#", "major", 0, "x68886")]
    [InlineData("E", "major", 0, "x79997")]
    [InlineData("F", "major", 0, "133211")]
    [InlineData("F#", "major", 0, "244322")]
    [InlineData("G", "major", 0, "355433")]
    [InlineData("G#", "major", 0, "466544")]
    [InlineData("A", "major", 0, "577655")]
    [InlineData("A#", "major", 0, "x13331")]
    [InlineData("B", "major", 0, "x24442")]
    [InlineData("C", "minor", 0, "x35543")]
    [InlineData("C#", "minor", 0, "x46654")]
    [InlineData("D", "minor", 0, "x57765")]
    [InlineData("D#", "minor", 0, "x68876")]
    [InlineData("E", "minor", 0, "x79987")]
    [InlineData("F", "minor", 0, "133111")]
    [InlineData("F#", "minor", 0, "244222")]
    [InlineData("G", "minor", 0, "355333")]
    [InlineData("G#", "minor", 0, "466444")]
    [InlineData("A", "minor", 0, "577555")]
    [InlineData("A#", "minor", 0, "x13321")]
    [InlineData("B", "minor", 0, "x24432")]
    // Inversions: C/G, D/A, E/B, G/D, A/E, Am/E.
    [InlineData("C", "major", 2, "335553")]
    [InlineData("D", "major", 2, "557775")]
    [InlineData("E", "major", 2, "779997")]
    [InlineData("G", "major", 2, "x55433")]
    [InlineData("A", "major", 2, "x77655")]
    [InlineData("A", "minor", 2, "x77555")]
    // Diminished and seventh chords.
    [InlineData("C", "diminished", 0, "x3454x")]
    [InlineData("D", "diminished", 0, "x5676x")]
    [InlineData("B", "diminished", 0, "x2343x")]
    [InlineData("C", "major7", 0, "x35453")]
    [InlineData("G", "major7", 0, "354433")]
    [InlineData("A", "major7", 0, "576655")]
    [InlineData("A", "minor7", 0, "575555")]
    [InlineData("B", "minor7", 0, "x24232")]
    [InlineData("C", "dominant7", 0, "x35353")]
    [InlineData("D", "dominant7", 0, "x57575")]
    [InlineData("E", "dominant7", 0, "x79797")]
    [InlineData("G", "dominant7", 0, "353433")]
    [InlineData("A", "dominant7", 0, "575655")]
    public void Find_AsBarreChords_PlaysTheShapeGuitaristsPlay(string root, string quality, int inversion, string shape)
    {
        (GuitarVoicing.Find(Chord(root, quality, inversion), GuitarPosition.Barre)?.ToString()).Should().Be(shape);
    }

    [Theory]
    // From the 7th fret up, on the highest strings.
    [InlineData("C", "major", 0, "xx(10)988")]
    [InlineData("C#", "major", 0, "xx(11)(10)99")]
    [InlineData("D", "major", 0, "xx(12)(11)(10)(10)")]
    [InlineData("D#", "major", 0, "(11)(10)888(11)")]
    [InlineData("E", "major", 0, "x79997")]
    [InlineData("F", "major", 0, "x8(10)(10)(10)8")]
    [InlineData("F#", "major", 0, "x9(11)(11)(11)9")]
    [InlineData("G", "major", 0, "x(10)(12)(12)(12)(10)")]
    [InlineData("G#", "major", 0, "x(11)(10)898")]
    [InlineData("A", "major", 0, "x(12)(11)9(10)9")]
    [InlineData("A#", "major", 0, "xx8(10)(11)(10)")]
    [InlineData("B", "major", 0, "xx9877")]
    [InlineData("C", "minor", 0, "xx(10)888")]
    [InlineData("D", "minor", 0, "xx(12)(10)(10)(10)")]
    [InlineData("E", "minor", 0, "x79987")]
    [InlineData("F", "minor", 0, "x8(10)(10)98")]
    [InlineData("G", "minor", 0, "x(10)(12)(12)(11)(10)")]
    [InlineData("A", "minor", 0, "xx79(10)8")]
    [InlineData("B", "minor", 0, "xx9777")]
    // Inversions: C/G, D/F#, A/C#, Am/C.
    [InlineData("C", "major", 2, "x(10)(10)988")]
    [InlineData("D", "major", 1, "x9777x")]
    [InlineData("A", "major", 1, "xx(11)9(10)9")]
    [InlineData("A", "minor", 1, "xx(10)9(10)8")]
    // Seventh chords.
    [InlineData("C", "major7", 0, "8(10)9988")]
    [InlineData("A", "minor7", 0, "xx7988")]
    [InlineData("B", "minor7", 0, "797777")]
    [InlineData("C", "dominant7", 0, "8(10)8988")]
    [InlineData("G", "dominant7", 0, "x(10)(12)(10)(12)(10)")]
    [InlineData("A", "dominant7", 0, "xx7989")]
    // The few chords a hand can't hold from the 7th fret up come down as little as they need.
    [InlineData("D#", "diminished", 0, "x6787x")]
    [InlineData("G#", "dominant7", 0, "xx6878")]
    [InlineData("E", "diminished7", 0, "x78686")]
    public void Find_HighOnTheNeck_PlaysTheShapeGuitaristsPlay(string root, string quality, int inversion, string shape)
    {
        (GuitarVoicing.Find(Chord(root, quality, inversion), GuitarPosition.High)?.ToString()).Should().Be(shape);
    }

    // Every chord in every position on the neck.
    public static TheoryData<string, string, int, GuitarPosition> EveryChord
    {
        get
        {
            var data = new TheoryData<string, string, int, GuitarPosition>();
            foreach (var root in Roots)
            {
                foreach (var (quality, semitones) in Qualities)
                {
                    for (var inversion = 0; inversion < semitones.Length; inversion++)
                    {
                        foreach (var position in Enum.GetValues<GuitarPosition>())
                        {
                            data.Add(root, quality, inversion, position);
                        }
                    }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryChord))]
    public void Find_PlaysEveryChord_InEveryPosition_OnAShapeOneHandCanHold(
        string root, string quality, int inversion, GuitarPosition position)
    {
        ShouldPlayOnAShapeOneHandCanHold(Chord(root, quality, inversion), position);
    }

    // The sus, 6 and add9 chords of every root in every position on the neck.
    public static TheoryData<string, string, GuitarPosition> EverySusAndAddedChord
    {
        get
        {
            var data = new TheoryData<string, string, GuitarPosition>();
            foreach (var root in Roots)
            {
                foreach (var quality in SusAndAddedChords.Keys)
                {
                    foreach (var position in Enum.GetValues<GuitarPosition>())
                    {
                        data.Add(root, quality, position);
                    }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EverySusAndAddedChord))]
    public void Find_PlaysTheSusSixthAndAdd9Chords_InEveryPosition_OnAShapeOneHandCanHold(
        string root, string quality, GuitarPosition position)
    {
        ShouldPlayOnAShapeOneHandCanHold(Chord(root, quality, 0), position);
    }

    private static void ShouldPlayOnAShapeOneHandCanHold(List<int> chord, GuitarPosition position)
    {
        var shape = GuitarVoicing.Find(chord, position);

        shape.Should().NotBeNull();
        var strings = Enumerable.Range(0, 6).Where(s => shape!.Frets[s] is not null).ToList();
        strings.Should().HaveCountGreaterThanOrEqualTo(4, "a strum sounds four to six strings")
            .And.Equal(Enumerable.Range(strings[0], strings.Count), "the strum crosses every string it sounds");
        shape!.Notes.Should().BeInAscendingOrder("a downstroke strums the bass first")
            .And.OnlyHaveUniqueItems()
            .And.AllSatisfy(note => note.Should().BeInRange(40, 76, "the notes are on the neck"));
        shape.Notes.Select(PitchClass).Distinct().Should().BeEquivalentTo(chord.Select(PitchClass).Distinct(),
            "the shape plays every note of the chord, and only them");
        PitchClass(shape.Notes[0]).Should().Be(PitchClass(chord.Min()), "an inversion stays one");

        var fretted = shape.Frets.Where(fret => fret > 0).Select(fret => fret!.Value).ToList();
        if (fretted.Count > 0)
            (fretted.Max() - fretted.Min()).Should().BeLessThanOrEqualTo(3, "a hand spans four frets");

        if (position != GuitarPosition.Open)
            shape.Frets.Should().NotContain(0, "only the open chords let open strings ring");
    }

    // The triads of the exercises: every inversion of the major and minor ones (GuessInversion),
    // and the diminished and augmented ones (GuessChords, GuessQuality, GuessFunction).
    public static TheoryData<string, string, int> ExerciseTriads => Triads("diminished", "augmented");

    // The same but the diminished ones, two of which a hand can't hold from the 7th fret up.
    public static TheoryData<string, string, int> ExerciseTriadsButTheDiminishedOnes => Triads("augmented");

    [Theory]
    [MemberData(nameof(ExerciseTriads))]
    public void Find_OnTheOpenChords_PlaysTheTriadsOfTheExercises_WithTheBassAsLowAsTheNeckHasIt(
        string root, string quality, int inversion)
    {
        GuitarVoicing.Find(Chord(root, quality, inversion), GuitarPosition.Open)!.Notes[0]
            .Should().BeInRange(LowestBass, HighestOpenChordBass, "the neck has a shape of each of them with its bass from E2 to D#3");
    }

    [Theory]
    [MemberData(nameof(ExerciseTriadsButTheDiminishedOnes))]
    public void Find_HighOnTheNeck_PlaysTheTriadsOfTheExercises_FromThe7thFretUp(string root, string quality, int inversion)
    {
        GuitarVoicing.Find(Chord(root, quality, inversion), GuitarPosition.High)!.Frets
            .Should().AllSatisfy(fret => (fret ?? HighPositionFret).Should().BeGreaterThanOrEqualTo(HighPositionFret));
    }

    [Fact]
    public void Find_PutsTheLowestNoteInTheBass_WhateverTheOrderOfTheNotes()
    {
        (GuitarVoicing.Find([76, 36, 91])?.ToString()).Should().Be("x32010", "C is the lowest note");
        (GuitarVoicing.Find([72, 64, 67])?.ToString()).Should().Be("032010", "E is the lowest note");
        (GuitarVoicing.Find([72, 64, 67], GuitarPosition.Barre)?.ToString()).Should().Be("x7555x", "E is the lowest note");
    }

    [Theory]
    [InlineData(GuitarPosition.Open)]
    [InlineData(GuitarPosition.Barre)]
    [InlineData(GuitarPosition.High)]
    public void Find_PlaysNothing_WithoutNotes(GuitarPosition position)
    {
        GuitarVoicing.Find([], position).Should().BeNull();
    }

    [Theory]
    [InlineData("Open", GuitarPosition.Open)]
    [InlineData("Barre", GuitarPosition.Barre)]
    [InlineData("High", GuitarPosition.High)]
    [InlineData("barre", GuitarPosition.Barre)]
    [InlineData(" HIGH ", GuitarPosition.High)]
    [InlineData(null, GuitarPosition.Open)]
    [InlineData("", GuitarPosition.Open)]
    [InlineData("Sideways", GuitarPosition.Open)]
    [InlineData("1", GuitarPosition.Open)]
    public void PositionFromName_ReadsTheCookie_AndOtherwisePlaysTheOpenChords(string? name, GuitarPosition position)
    {
        GuitarVoicing.PositionFromName(name).Should().Be(position);
    }

    [Fact]
    public void AShape_PlaysTheStringsItFrets()
    {
        var c = new GuitarShape([null, 3, 2, 0, 1, 0]);

        c.Notes.Should().Equal(48, 52, 55, 60, 64);
        c.ToString().Should().Be("x32010");
        new GuitarShape([null, null, 10, 12, 12, 10]).ToString().Should().Be("xx(10)(12)(12)(10)");
        FluentActions.Invoking(() => new GuitarShape([0, 2, 2, 1, 0])).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// MIDI notes of the <paramref name="quality"/> chord on <paramref name="root"/>, in octave 3
    /// with its lowest <paramref name="inversion"/> notes an octave up.
    /// </summary>
    private static List<int> Chord(string root, string quality, int inversion)
    {
        var notes = (Qualities.GetValueOrDefault(quality) ?? SusAndAddedChords[quality])
            .Select(semitones => 48 + Array.IndexOf(Roots, root) + semitones).ToList();
        for (var i = 0; i < inversion; i++)
        {
            notes[i] += 12;
        }
        return notes;
    }

    /// <summary>
    /// Every root and inversion of the major and minor triads, and the root position of the
    /// <paramref name="rootPositionQualities"/> triads.
    /// </summary>
    private static TheoryData<string, string, int> Triads(params string[] rootPositionQualities)
    {
        var data = new TheoryData<string, string, int>();
        foreach (var root in Roots)
        {
            for (var inversion = 0; inversion < 3; inversion++)
            {
                data.Add(root, "major", inversion);
                data.Add(root, "minor", inversion);
            }
            foreach (var quality in rootPositionQualities)
            {
                data.Add(root, quality, 0);
            }
        }
        return data;
    }

    private static int PitchClass(int midi) => midi % 12;
}
