using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The guitar plays a chord the way a guitarist does: a shape of the neck that one hand can
/// hold, strumming four to six strings from the chord's bass up.
/// </summary>
public class GuitarVoicingTests
{
    private static readonly string[] Roots = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // Semitones from the root to each note of the chords the exercises play (GuessChords, GuessFunction,
    // GuessQuality, GuessInversion and GuessCadence), and of the other common seventh chords.
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

    [Theory]
    // Open chords and barre chords.
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
    [InlineData("C#", "minor", 1, "xx2120")]
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
    public void Find_PlaysTheShapeGuitaristsPlay(string root, string quality, int inversion, string shape)
    {
        (GuitarVoicing.Find(Chord(root, quality, inversion))?.ToString()).Should().Be(shape);
    }

    public static TheoryData<string, string, int> EveryChord
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            foreach (var root in Roots)
            {
                foreach (var (quality, semitones) in Qualities)
                {
                    for (var inversion = 0; inversion < semitones.Length; inversion++)
                    {
                        data.Add(root, quality, inversion);
                    }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryChord))]
    public void Find_PlaysEveryChord_OnAShapeOneHandCanHold(string root, string quality, int inversion)
    {
        var chord = Chord(root, quality, inversion);

        var shape = GuitarVoicing.Find(chord);

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
    }

    [Fact]
    public void Find_KeepsTheLowestNoteInTheBass_InAnyOctaveAndOrder()
    {
        (GuitarVoicing.Find([76, 36, 91])?.ToString()).Should().Be("x32010", "C is the lowest note");
        (GuitarVoicing.Find([72, 64, 67])?.ToString()).Should().Be("032010", "E is the lowest note");
    }

    [Fact]
    public void Find_PlaysNothing_WithoutNotes()
    {
        GuitarVoicing.Find([]).Should().BeNull();
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
        var notes = Qualities[quality].Select(semitones => 48 + Array.IndexOf(Roots, root) + semitones).ToList();
        for (var i = 0; i < inversion; i++)
        {
            notes[i] += 12;
        }
        return notes;
    }

    private static int PitchClass(int midi) => midi % 12;
}
