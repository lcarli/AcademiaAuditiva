using System.Text.Json;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Xunit.Abstractions;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The Explore page plays only what <see cref="ExploreSoundBuilder"/> lists. Every listed
/// choice must sound (each note has a piano sample) and be spelled as a musician writes
/// it; anything else is refused.
/// </summary>
public class ExploreSoundBuilderTests
{
    private const double Tolerance = 1e-9;

    private readonly ExploreSoundBuilder _builder = new();
    private readonly ITestOutputHelper _output;

    public ExploreSoundBuilderTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryListedChoice_PlaysItsNotes_FromThePianoSamples()
    {
        foreach (var request in AllChoices())
        {
            var because = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
            var sound = _builder.Build(request);

            sound.Should().NotBeNull(because);
            var midis = sound!.Notes.Select(MusicTheoryService.NoteToMidi).ToList();
            midis.Should().NotBeEmpty(because).And.OnlyContain(m => m.HasValue && PianoSamples.Covers(m.Value), because);
            sound.Plan.Select(i => i.BlobName).Should().Equal(midis.Select(m => PianoSamples.BlobName(m!.Value)), because);
        }
    }

    // The mixer remembers every plan it mixed, and storage keeps each mix for about a
    // day, so the lists must stay small. Choices that sound the same share one mix
    // (an augmented fourth is a diminished fifth).
    [Fact]
    public void ListedChoices_NeedOnlyAFewThousandMixes()
    {
        var choices = AllChoices().ToList();
        var mixes = choices
            .Select(request => string.Join('|', _builder.Build(request)!.Plan.Select(i =>
                FormattableString.Invariant($"{i.BlobName}@{i.StartTimeSeconds:F4}/{i.DurationSeconds:F4}"))))
            .ToHashSet();

        _output.WriteLine($"{choices.Count} choices, {mixes.Count} distinct mixes");
        mixes.Count.Should().BeLessThan(10_000);
    }

    [Theory]
    [InlineData("""{"kind":"note","root":"F#","octave":4}""", "F#", "F#4")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"interval":"3min","direction":"ascending"}""", "C", "C4,Eb4")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"interval":"3maj","direction":"descending"}""", "C", "C4,Ab3")]
    // A black-key root takes the name with the simpler notes…
    [InlineData("""{"kind":"interval","root":"F#","octave":4,"interval":"4A","direction":"ascending"}""", "Gb", "Gb4,C5")]
    [InlineData("""{"kind":"interval","root":"F#","octave":4,"interval":"5dim","direction":"ascending"}""", "F#", "F#4,C5")]
    [InlineData("""{"kind":"chord","root":"C#","octave":4,"quality":"major"}""", "Db", "Db4,F4,Ab4")]
    [InlineData("""{"kind":"chord","root":"C#","octave":4,"quality":"minor"}""", "C#", "C#4,E4,G#4")]
    [InlineData("""{"kind":"scale","root":"G#","octave":6,"scale":"major","direction":"ascending"}""", "Ab", "Ab6,Bb6,C7,Db7,Eb7,F7,G7,Ab7")]
    // …and keeps its sharp on a tie.
    [InlineData("""{"kind":"interval","root":"C#","octave":4,"interval":"5J","direction":"harmonic"}""", "C#", "C#4,G#4")]
    [InlineData("""{"kind":"scale","root":"F#","octave":5,"scale":"major","direction":"ascending"}""", "F#", "F#5,G#5,A#5,B5,C#6,D#6,E#6,F#6")]
    // Inversions move the lowest notes up an octave; they keep their names.
    [InlineData("""{"kind":"chord","root":"F#","octave":4,"quality":"major","inversion":2}""", "F#", "C#5,F#5,A#5")]
    [InlineData("""{"kind":"chord","root":"F#","octave":4,"quality":"dominant7","inversion":3}""", "F#", "E5,F#5,A#5,C#6")]
    [InlineData("""{"kind":"chord","root":"B","octave":3,"quality":"diminished7"}""", "B", "B3,D4,F4,Ab4")]
    [InlineData("""{"kind":"scale","root":"F#","octave":5,"scale":"dorian","direction":"descending"}""", "F#", "F#6,E6,D#6,C#6,B5,A5,G#5,F#5")]
    [InlineData("""{"kind":"scale","root":"A","octave":3,"scale":"minorPentatonic","direction":"ascending"}""", "A", "A3,C4,D4,E4,G4,A4")]
    public void Build_SpellsTheNotesAsTheyAreWritten(string json, string root, string notes)
    {
        var sound = Build(json);

        sound.Root.Should().Be(root);
        sound.Notes.Should().Equal(notes.Split(','));
    }

    [Fact]
    public void Note_PlaysItsSampleForTwoSeconds()
    {
        var sound = Build("""{"kind":"note","root":"A#","octave":2}""");

        sound.Notes.Should().Equal("A#2");
        sound.Simultaneous.Should().BeFalse();
        sound.Plan.Should().Equal(new MixInput("As2.mp3", 0, 2.0));
    }

    [Fact]
    public void MelodicInterval_PlaysTheSecondNoteAfterTheFirst()
    {
        var sound = Build("""{"kind":"interval","root":"C","octave":4,"interval":"5J","direction":"ascending"}""");

        sound.Simultaneous.Should().BeFalse();
        sound.Plan.Should().Equal(new MixInput("C4.mp3", 0, 1.5), new MixInput("G4.mp3", 1.75, 2.0));
    }

    [Fact]
    public void HarmonicIntervalAndBlockChord_PlayTheirNotesTogether()
    {
        var interval = Build("""{"kind":"interval","root":"C","octave":4,"interval":"5J","direction":"harmonic"}""");
        var chord = Build("""{"kind":"chord","root":"C","octave":4,"quality":"major"}""");

        interval.Simultaneous.Should().BeTrue();
        interval.Plan.Should().Equal(new MixInput("C4.mp3", 0, 2.0), new MixInput("G4.mp3", 0, 2.0));
        chord.Simultaneous.Should().BeTrue();
        chord.Plan.Should().Equal(new MixInput("C4.mp3", 0, 2.0), new MixInput("E4.mp3", 0, 2.0), new MixInput("G4.mp3", 0, 2.0));
    }

    [Fact]
    public void Arpeggio_StartsTheNotesInTurn_AndEndsThemTogether()
    {
        var sound = Build("""{"kind":"chord","root":"C","octave":4,"quality":"major7","arpeggio":true}""");

        sound.Simultaneous.Should().BeFalse();
        sound.Notes.Should().Equal("C4", "E4", "G4", "B4");
        sound.Plan.Select(i => i.BlobName).Should().Equal("C4.mp3", "E4.mp3", "G4.mp3", "B4.mp3");
        sound.Plan.Select(i => i.StartTimeSeconds).Should().Equal(new[] { 0.0, 0.4, 0.8, 1.2 }, Close);
        sound.Plan.Select(i => i.StartTimeSeconds + i.DurationSeconds!.Value).Should().AllSatisfy(end => end.Should().BeApproximately(3.2, Tolerance));
    }

    [Fact]
    public void Scale_PlaysOneNoteAfterAnother_AndHoldsTheLast()
    {
        var sound = Build("""{"kind":"scale","root":"C","octave":4,"scale":"major","direction":"ascending"}""");

        sound.Simultaneous.Should().BeFalse();
        sound.Plan.Select(i => i.StartTimeSeconds).Should().Equal(Enumerable.Range(0, 8).Select(i => i * 0.85), Close);
        sound.Plan.Select(i => i.DurationSeconds).Should().Equal(0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 1.5);
    }

    [Theory]
    [InlineData("""{"root":"C","octave":4}""")]
    [InlineData("""{"kind":"melody","root":"C","octave":4}""")]
    [InlineData("""{"kind":"Note","root":"C","octave":4}""")]
    [InlineData("""{"kind":"note","octave":4}""")]
    [InlineData("""{"kind":"note","root":"Db","octave":4}""")]          // roots are named with sharps
    [InlineData("""{"kind":"note","root":"H","octave":4}""")]
    [InlineData("""{"kind":"note","root":"C4","octave":4}""")]
    [InlineData("""{"kind":"note","root":"C","octave":0}""")]           // no sample below C1…
    [InlineData("""{"kind":"note","root":"C","octave":8}""")]           // …or above B7
    [InlineData("""{"kind":"interval","root":"C","octave":7,"interval":"5J","direction":"ascending"}""")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"interval":"9maj","direction":"ascending"}""")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"direction":"ascending"}""")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"interval":"5J"}""")]
    [InlineData("""{"kind":"interval","root":"C","octave":4,"interval":"5J","direction":"up"}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":6,"quality":"major"}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":4}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"power"}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"major","inversion":3}""")]   // a triad has three positions
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"major7","inversion":4}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"major","inversion":-1}""")]
    [InlineData("""{"kind":"scale","root":"C","octave":1,"scale":"major","direction":"ascending"}""")]
    [InlineData("""{"kind":"scale","root":"C","octave":4,"direction":"ascending"}""")]
    [InlineData("""{"kind":"scale","root":"C","octave":4,"scale":"blues","direction":"ascending"}""")]
    [InlineData("""{"kind":"scale","root":"C","octave":4,"scale":"major"}""")]
    [InlineData("""{"kind":"scale","root":"C","octave":4,"scale":"major","direction":"harmonic"}""")]   // scales are melodic
    public void Build_RefusesAnythingOffTheLists(string json)
    {
        _builder.Build(Parse(json)).Should().BeNull();
    }

    [Fact]
    public void Build_NeedsARequest()
    {
        FluentActions.Invoking(() => _builder.Build(null!)).Should().Throw<ArgumentNullException>();
    }

    private static bool Close(double actual, double expected) => Math.Abs(actual - expected) < Tolerance;

    private static ExplorePlayRequest Parse(string json) => JsonSerializer.Deserialize<ExplorePlayRequest>(json, JsonSerializerOptions.Web)!;

    private ExploreSound Build(string json)
    {
        var sound = _builder.Build(Parse(json));
        sound.Should().NotBeNull(json);
        return sound!;
    }

    private static IEnumerable<ExplorePlayRequest> AllChoices()
    {
        foreach (var root in ExploreSoundBuilder.Roots)
        {
            foreach (var octave in Octaves(ExploreSoundBuilder.NoteKind))
            {
                yield return new() { Kind = ExploreSoundBuilder.NoteKind, Root = root, Octave = octave };
            }

            foreach (var octave in Octaves(ExploreSoundBuilder.IntervalKind))
            foreach (var interval in ExploreSoundBuilder.IntervalCodes)
            foreach (var direction in new[] { ExploreSoundBuilder.Ascending, ExploreSoundBuilder.Descending, ExploreSoundBuilder.Harmonic })
            {
                yield return new() { Kind = ExploreSoundBuilder.IntervalKind, Root = root, Octave = octave, Interval = interval, Direction = direction };
            }

            foreach (var octave in Octaves(ExploreSoundBuilder.ChordKind))
            foreach (var (quality, tones) in ExploreSoundBuilder.ChordQualities)
            foreach (var inversion in Enumerable.Range(0, tones))
            foreach (var arpeggio in new[] { false, true })
            {
                yield return new() { Kind = ExploreSoundBuilder.ChordKind, Root = root, Octave = octave, Quality = quality, Inversion = inversion, Arpeggio = arpeggio };
            }

            foreach (var octave in Octaves(ExploreSoundBuilder.ScaleKind))
            foreach (var scale in ExploreSoundBuilder.ScaleNames.Concat(ExploreSoundBuilder.ModeNames))
            foreach (var direction in new[] { ExploreSoundBuilder.Ascending, ExploreSoundBuilder.Descending })
            {
                yield return new() { Kind = ExploreSoundBuilder.ScaleKind, Root = root, Octave = octave, Scale = scale, Direction = direction };
            }
        }
    }

    private static IEnumerable<int> Octaves(string kind)
    {
        var (min, max) = ExploreSoundBuilder.OctavesFor(kind);
        return Enumerable.Range(min, max - min + 1);
    }
}
