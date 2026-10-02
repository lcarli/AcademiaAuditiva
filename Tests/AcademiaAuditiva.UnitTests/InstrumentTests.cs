using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The instrument comes from a cookie, so any value must fall back to the piano, and the
/// note range it allows must stay inside what the range sliders and samples offer.
/// </summary>
public class InstrumentTests
{
    [Theory]
    [InlineData("Piano", "Piano")]
    [InlineData("Guitar", "Guitar")]
    [InlineData("guitar", "Guitar")]
    [InlineData(" VIOLIN ", "Violin")]
    [InlineData(null, "Piano")]
    [InlineData("", "Piano")]
    [InlineData("Drums", "Piano")]
    [InlineData("guitar/..", "Piano")]
    public void FromName_FindsTheInstrument_OrFallsBackToThePiano(string? name, string expected)
    {
        Instrument.FromName(name).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Piano", "Piano")]
    [InlineData("guitar", "Guitar")]
    [InlineData("Violin", "Piano")]
    [InlineData(" violin ", "Piano")]
    [InlineData(null, "Piano")]
    [InlineData("Drums", "Piano")]
    public void FromName_ForChords_FallsBackToThePiano_WhenTheInstrumentPlaysNone(string? name, string expected)
    {
        Instrument.FromName(name, chords: true).Name.Should().Be(expected);
    }

    [Fact]
    public void ExercisesThatPlayChords_OfferOnlyTheInstrumentsThatPlayThem()
    {
        Instrument.Piano.Chords.Should().Be(ChordStyle.Together);
        Instrument.Guitar.Chords.Should().Be(ChordStyle.Strummed);
        Instrument.Violin.PlaysChords.Should().BeFalse("a violin plays one note at a time");
        Instrument.Offered(chords: true).Should().Equal(Instrument.Piano, Instrument.Guitar);
        Instrument.Offered(chords: false).Should().Equal(Instrument.All);
    }

    [Theory]
    [InlineData("Piano", "C4", "C4.mp3")]
    [InlineData("Guitar", "C#4", "guitar/Cs4.mp3")]
    [InlineData("Guitar", "Db4", "guitar/Cs4.mp3")]
    [InlineData("Guitar", "E2", "guitar/E2.mp3")]
    [InlineData("Violin", "B#5", "violin/C6.mp3")]
    [InlineData("Violin", "Bb6", "violin/As6.mp3")]
    public void SampleFor_PlaysEverySpellingOfAPitch_FromTheInstrumentFolder(string instrument, string note, string expected)
    {
        Instrument.FromName(instrument).SampleFor(note).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C")]
    [InlineData("H4")]
    [InlineData("C4.mp3")]
    public void SampleFor_RefusesWhatIsNotANote(string note)
    {
        FluentActions.Invoking(() => Instrument.Guitar.SampleFor(note)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Piano", "C3-C5", "C3-C5")]
    [InlineData("Piano", "C5-C3", "C3-C5")]
    [InlineData("Piano", "C0-C9", "C1-C6")]
    [InlineData("Piano", null, "C4-C4")]
    [InlineData("Guitar", "C1-C6", "C2-C5")]
    [InlineData("Guitar", "C6-C6", "C5-C5")]
    [InlineData("Guitar", null, "C4-C4")]
    [InlineData("Violin", "C2-C3", "C3-C3")]
    [InlineData("Violin", "C3-C5", "C3-C5")]
    [InlineData("Violin", "not-a-range", "C4-C4")]
    public void ClampRange_KeepsTheRange_WhereTheInstrumentSoundsNatural(string instrument, string? noteRange, string expected)
    {
        Instrument.FromName(instrument).ClampRange(noteRange).Should().Be(expected);
    }

    [Theory]
    [InlineData("Piano", "C1", "B6", 24, 95, 1, 6)]
    [InlineData("Guitar", "E2", "B5", 40, 83, 2, 5)]
    [InlineData("Violin", "G3", "B6", 55, 95, 3, 6)]
    public void EveryInstrument_PlaysTheNotesWhereItSoundsNatural(
        string instrument, string lowestNote, string highestNote, int lowestMidi, int highestMidi, int lowestOctave, int highestOctave)
    {
        // The guitar from its low E string to the 19th fret of its high E string, the violin from its G string.
        var i = Instrument.FromName(instrument);

        (i.LowestNote, i.HighestNote).Should().Be((lowestNote, highestNote));
        (i.LowestMidi, i.HighestMidi).Should().Be((lowestMidi, highestMidi));
        (i.LowestOctave, i.HighestOctave).Should().Be((lowestOctave, highestOctave), "the sliders offer the octaves of those notes");
    }

    [Theory]
    [InlineData("Guitar", "E2", true)]
    [InlineData("Guitar", "Fb2", true)]
    [InlineData("Guitar", "D#2", false)]
    [InlineData("Guitar", "B5", true)]
    [InlineData("Guitar", "C6", false)]
    [InlineData("Violin", "G3", true)]
    [InlineData("Violin", "F#3", false)]
    [InlineData("Piano", "C1", true)]
    [InlineData("Piano", "C7", false)]
    [InlineData("Piano", "not-a-note", false)]
    public void Has_TheNotesOfItsRange(string instrument, string note, bool has)
    {
        Instrument.FromName(instrument).Has(note).Should().Be(has);
    }

    [Fact]
    public void NotesIn_LeavesOutTheNotesTheInstrumentDoesNotHave()
    {
        Instrument.Guitar.NotesIn([2]).Should().Equal("E2", "F2", "F#2", "G2", "G#2", "A2", "A#2", "B2");
        Instrument.Violin.NotesIn([3, 4]).Should().HaveCount(5 + 12).And.StartWith(["G3", "G#3", "A3", "A#3", "B3", "C4"]);
        Instrument.Piano.NotesIn([1, 2, 3, 4, 5, 6]).Should().Equal(MusicTheoryService.GetAllNotes([1, 2, 3, 4, 5, 6]));
    }

    [Theory]
    [InlineData("Guitar", "C1-C6", new[] { 2, 3, 4, 5 })]
    [InlineData("Guitar", "C2-C2", new[] { 2 })]
    [InlineData("Violin", "C1-C2", new[] { 3 })]
    [InlineData("Violin", null, new[] { 4 })]
    [InlineData("Piano", "C6-C1", new[] { 1, 2, 3, 4, 5, 6 })]
    public void Octaves_AreTheOctavesOfTheRange_KeptWithinTheInstrumentOnes(string instrument, string? noteRange, int[] octaves)
    {
        Instrument.FromName(instrument).Octaves(noteRange).Should().Equal(octaves);
    }

    [Theory]
    [InlineData("Guitar", 2, "E2")]
    [InlineData("Guitar", 3, "C3")]
    [InlineData("Guitar", 5, "C5")]
    [InlineData("Violin", 3, "G3")]
    [InlineData("Violin", 4, "C4")]
    [InlineData("Piano", 1, "C1")]
    [InlineData("Piano", 4, "C4")]
    public void OctaveLabel_NamesTheOctave_ByItsFirstNoteTheInstrumentHas(string instrument, int octave, string label)
    {
        Instrument.FromName(instrument).OctaveLabel(octave).Should().Be(label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C4-C4")]
    [InlineData("C1-C6")]
    [InlineData("C6-C1")]
    [InlineData("C0-C99")]
    [InlineData("C1-C2147483647")]
    [InlineData("x")]
    public void ClampRange_NeverChangesThePianoOctaves(string? noteRange)
    {
        MusicTheoryService.ParseOctaveRange(Instrument.Piano.ClampRange(noteRange))
            .Should().Equal(MusicTheoryService.ParseOctaveRange(noteRange));
    }

    [Fact]
    public void EveryInstrument_HasItsOwnName_AndOctavesTheSlidersOffer()
    {
        Instrument.All.Select(i => i.Name).Should().OnlyHaveUniqueItems();
        Instrument.All[0].Should().Be(Instrument.Piano, "the piano is the default");
        Instrument.Bundled.Should().Equal(Instrument.Guitar, Instrument.Violin);
        Instrument.All.Should().AllSatisfy(i =>
        {
            i.LowestOctave.Should().BeInRange(MusicTheoryService.MinRangeOctave, i.HighestOctave);
            i.HighestOctave.Should().BeLessThanOrEqualTo(MusicTheoryService.MaxRangeOctave);
            i.LowestMidi.Should().BeInRange(PianoSamples.LowestMidi, i.HighestMidi, "every note has a sample");
            i.HighestMidi.Should().BeLessThanOrEqualTo(PianoSamples.HighestMidi, "every note has a sample");
            MusicTheoryService.DefaultRangeOctave.Should().BeInRange(i.LowestOctave, i.HighestOctave, "the sliders start on it");
        });
    }
}
