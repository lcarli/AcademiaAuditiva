using System.Globalization;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// An instrument the exercises can be played on. Every instrument has a sample
/// for each semitone the piano has (C1 to B7, see <see cref="PianoSamples"/>),
/// but the notes a student can pick with the note range stay where the
/// instrument sounds natural, from <see cref="LowestNote"/> to
/// <see cref="HighestNote"/>.
/// </summary>
/// <param name="Name">Value of the <c>instrument</c> cookie and filter.</param>
/// <param name="Folder">
/// Folder of the samples that ship with the app (<see cref="BundledSamples"/>),
/// or <c>null</c> for the piano, whose samples are in the <c>piano-audio</c> container.
/// </param>
/// <param name="LowestNote">Lowest note of the note range (<c>E2</c>).</param>
/// <param name="HighestNote">Highest note of the note range (<c>B5</c>).</param>
/// <param name="Chords">How the instrument plays the notes of a chord.</param>
public sealed record Instrument(string Name, string? Folder, string LowestNote, string HighestNote, ChordStyle Chords)
{
    /// <summary>Piano, over every octave of the note range sliders.</summary>
    public static readonly Instrument Piano = new("Piano", null, "C1", "B6", ChordStyle.Together);

    /// <summary>
    /// Nylon-string guitar, from its low E string (E2) to the 19th fret of its high E string
    /// (B5). It plays the chords where on the neck the student picks (<see cref="GuitarPosition"/>).
    /// </summary>
    public static readonly Instrument Guitar = new("Guitar", "guitar", "E2", "B5", ChordStyle.Strummed);

    /// <summary>Violin, from its G string (G3) to the top of the note range sliders.</summary>
    public static readonly Instrument Violin = new("Violin", "violin", "G3", "B6", ChordStyle.None);

    /// <summary>Every instrument, in the order the filters offer them.</summary>
    public static IReadOnlyList<Instrument> All { get; } = [Piano, Guitar, Violin];

    /// <summary>The instruments whose samples ship with the app.</summary>
    public static IReadOnlyList<Instrument> Bundled { get; } = [.. All.Where(i => i.Folder is not null)];

    private static readonly IReadOnlyList<Instrument> ChordInstruments = [.. All.Where(i => i.PlaysChords)];

    /// <summary>Resource key of the instrument's name.</summary>
    public string LabelKey => "Instrument." + Name;

    /// <summary>Whether the instrument plays chords: the violin plays one note at a time.</summary>
    public bool PlaysChords => Chords != ChordStyle.None;

    /// <summary>MIDI note of <see cref="LowestNote"/>.</summary>
    public int LowestMidi => Midi(LowestNote);

    /// <summary>MIDI note of <see cref="HighestNote"/>.</summary>
    public int HighestMidi => Midi(HighestNote);

    /// <summary>Lowest octave the note range sliders offer: the octave of <see cref="LowestNote"/>.</summary>
    public int LowestOctave => LowestMidi / 12 - 1;

    /// <summary>Highest octave the note range sliders offer: the octave of <see cref="HighestNote"/>.</summary>
    public int HighestOctave => HighestMidi / 12 - 1;

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
    /// when there is none, or it is malformed, the octave the sliders start on
    /// (<see cref="MusicTheoryService.DefaultRangeOctave"/>).
    /// </summary>
    public string ClampRange(string? noteRange)
    {
        var octaves = MusicTheoryService.ParseOctaveRange(noteRange);
        var lowest = Math.Clamp(octaves[0], LowestOctave, HighestOctave);
        var highest = Math.Clamp(octaves[^1], LowestOctave, HighestOctave);
        return string.Create(CultureInfo.InvariantCulture, $"C{lowest}-C{highest}");
    }

    /// <summary>The octaves of <paramref name="noteRange"/>, kept within this instrument's (<see cref="ClampRange"/>).</summary>
    public List<int> Octaves(string? noteRange) => MusicTheoryService.ParseOctaveRange(ClampRange(noteRange));

    /// <summary>Whether <paramref name="note"/> (<c>C#4</c>) is in the instrument's note range.</summary>
    public bool Has(string note) =>
        MusicTheoryService.NoteToMidi(note) is { } midi && midi >= LowestMidi && midi <= HighestMidi;

    /// <summary>
    /// The notes of <paramref name="octaves"/> that the instrument has, from the lowest up:
    /// octave 2 of the guitar goes from E2 to B2.
    /// </summary>
    public List<string> NotesIn(IEnumerable<int> octaves) => [.. MusicTheoryService.GetAllNotes([.. octaves]).Where(Has)];

    /// <summary>
    /// How the note range sliders name <paramref name="octave"/>: by its first note that the
    /// instrument has, so C except in the lowest octave (E2 on the guitar, G3 on the violin).
    /// </summary>
    public string OctaveLabel(int octave) =>
        octave == LowestOctave ? LowestNote : string.Create(CultureInfo.InvariantCulture, $"C{octave}");

    private static int Midi(string note) =>
        MusicTheoryService.NoteToMidi(note) ?? throw new InvalidOperationException($"Invalid note name '{note}'.");
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
