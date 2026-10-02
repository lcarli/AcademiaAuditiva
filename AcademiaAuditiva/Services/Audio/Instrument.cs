using System.Globalization;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// An instrument the exercises can be played on. Every instrument has a sample
/// for each semitone the piano has (C1 to B7, see <see cref="PianoSamples"/>),
/// but the octaves a student can pick for the note range stay where the
/// instrument sounds natural, and start where it plays the exercise
/// (<see cref="StartOctave"/>).
/// </summary>
/// <param name="Name">Value of the <c>instrument</c> cookie and filter.</param>
/// <param name="Folder">
/// Folder of the samples that ship with the app (<see cref="BundledSamples"/>),
/// or <c>null</c> for the piano, whose samples are in the <c>piano-audio</c> container.
/// </param>
/// <param name="LowestOctave">Lowest octave of the note range.</param>
/// <param name="HighestOctave">Highest octave of the note range.</param>
/// <param name="Chords">How the instrument plays the notes of a chord.</param>
public sealed record Instrument(string Name, string? Folder, int LowestOctave, int HighestOctave, ChordStyle Chords)
{
    public static readonly Instrument Piano = new("Piano", null, MusicTheoryService.MinRangeOctave, MusicTheoryService.MaxRangeOctave, ChordStyle.Together);

    /// <summary>
    /// Nylon-string guitar, whose lowest note is E2. Its chords start in octave 2, the
    /// octave of the bass of the open chords (<see cref="GuitarVoicing"/>).
    /// </summary>
    public static readonly Instrument Guitar = new("Guitar", "guitar", 2, 5, ChordStyle.Strummed) { ChordOctave = 2 };

    /// <summary>Violin, whose lowest note is G3.</summary>
    public static readonly Instrument Violin = new("Violin", "violin", 4, 6, ChordStyle.None);

    /// <summary>Every instrument, in the order the filters offer them.</summary>
    public static IReadOnlyList<Instrument> All { get; } = [Piano, Guitar, Violin];

    /// <summary>The instruments whose samples ship with the app.</summary>
    public static IReadOnlyList<Instrument> Bundled { get; } = [.. All.Where(i => i.Folder is not null)];

    private static readonly IReadOnlyList<Instrument> ChordInstruments = [.. All.Where(i => i.PlaysChords)];

    /// <summary>Resource key of the instrument's name.</summary>
    public string LabelKey => "Instrument." + Name;

    /// <summary>Whether the instrument plays chords: the violin plays one note at a time.</summary>
    public bool PlaysChords => Chords != ChordStyle.None;

    /// <summary>Octave the note range starts on in the exercises about chords.</summary>
    public int ChordOctave { get; init; } = MusicTheoryService.DefaultRangeOctave;

    /// <summary>
    /// Octave the note range starts on, before the student moves the sliders, in an exercise
    /// about <paramref name="chords"/> (<see cref="ChordOctave"/>) or not.
    /// </summary>
    public int StartOctave(bool chords) => chords ? ChordOctave : MusicTheoryService.DefaultRangeOctave;

    /// <summary>
    /// The instruments an exercise offers, in the order of <see cref="All"/>: when it plays
    /// <paramref name="chords"/>, only those that play them.
    /// </summary>
    public static IReadOnlyList<Instrument> Offered(bool chords) => chords ? ChordInstruments : All;

    /// <summary>
    /// The instrument called <paramref name="name"/> in any case (it comes from a
    /// cookie or a request), or the piano when there is no such instrument, or when
    /// the exercise plays <paramref name="chords"/> and the instrument doesn't.
    /// </summary>
    public static Instrument FromName(string? name, bool chords = false)
    {
        var instrument = All.FirstOrDefault(i => string.Equals(i.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Piano;
        return chords && !instrument.PlaysChords ? Piano : instrument;
    }

    /// <summary>The sample the mixer plays for MIDI note <paramref name="midi"/>.</summary>
    public string SampleName(int midi) =>
        Folder is null ? PianoSamples.BlobName(midi) : $"{Folder}/{PianoSamples.BlobName(midi)}";

    /// <summary>
    /// The sample for <paramref name="note"/> (<c>C#4</c>, <c>Db4</c>, <c>B#3</c>…):
    /// every spelling of a pitch plays the same sample.
    /// </summary>
    public string SampleFor(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Note name must not be empty.", nameof(note));

        var midi = MusicTheoryService.NoteToMidi(note)
            ?? throw new ArgumentException($"Invalid note name '{note}'.", nameof(note));
        return SampleName(midi);
    }

    /// <summary>
    /// <paramref name="noteRange"/> (<c>C3-C5</c>, read like
    /// <see cref="MusicTheoryService.ParseOctaveRange"/>) kept within this instrument's octaves:
    /// when there is none, or it is malformed, the octave the range
    /// <see cref="StartOctave">starts on</see> in an exercise about <paramref name="chords"/> or not.
    /// </summary>
    public string ClampRange(string? noteRange, bool chords = false)
    {
        var octaves = MusicTheoryService.ParseOctaveRange(noteRange, StartOctave(chords));
        var lowest = Math.Clamp(octaves[0], LowestOctave, HighestOctave);
        var highest = Math.Clamp(octaves[^1], LowestOctave, HighestOctave);
        return string.Create(CultureInfo.InvariantCulture, $"C{lowest}-C{highest}");
    }
}

/// <summary>How an <see cref="Instrument"/> plays the notes of a chord.</summary>
public enum ChordStyle
{
    /// <summary>It doesn't: exercises that play chords leave it out and play on the piano.</summary>
    None,

    /// <summary>All the notes at once, as the exercise spells them.</summary>
    Together,

    /// <summary>Strummed from the low string up, on a chord shape of the neck (<see cref="GuitarVoicing"/>).</summary>
    Strummed,
}
