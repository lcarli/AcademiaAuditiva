namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// The <c>piano-audio</c> container holds one sample per semitone from C1
/// to B7, named after the sharp spelling with <c>s</c> for <c>#</c>
/// (<c>C1.mp3</c>, <c>Cs1.mp3</c> … <c>B7.mp3</c>).
/// </summary>
public static class PianoSamples
{
    /// <summary>MIDI number of the lowest sample (C1).</summary>
    public const int LowestMidi = 24;

    /// <summary>MIDI number of the highest sample (B7).</summary>
    public const int HighestMidi = 107;

    public static bool Covers(int midi) => midi is >= LowestMidi and <= HighestMidi;

    /// <summary>
    /// Blob name of the sample for <paramref name="midi"/>. Flats and double
    /// accidentals share the file of their sharp twin.
    /// </summary>
    public static string BlobName(int midi) => MusicTheoryService.MidiToNote(midi).Replace("#", "s") + ".mp3";
}
