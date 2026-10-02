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
    [InlineData("Violin", "C2-C3", "C4-C4")]
    [InlineData("Violin", "C3-C5", "C4-C5")]
    [InlineData("Violin", "not-a-range", "C4-C4")]
    public void ClampRange_KeepsTheRange_WhereTheInstrumentSoundsNatural(string instrument, string? noteRange, string expected)
    {
        Instrument.FromName(instrument).ClampRange(noteRange).Should().Be(expected);
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
            i.LowestOctave.Should().BeLessThanOrEqualTo(4, "the sliders start on octave 4");
            i.HighestOctave.Should().BeGreaterThanOrEqualTo(4, "the sliders start on octave 4");
        });
    }
}
