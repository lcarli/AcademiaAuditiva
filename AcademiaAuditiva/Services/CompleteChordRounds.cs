using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Services;

/// <summary>
/// The rounds of CompleteChord: a chord is played and the student writes it on the staff,
/// its notes stacked in thirds over the root (close root position), as it was played. The
/// filters pick the chords (<c>ccQuality</c>), whether their notes may take accidentals
/// (<c>ccAccidentals</c>), whether the root is given on the staff (<c>ccRoot</c>), and the
/// octave of the root (<c>ccOctave</c>), which also sets the clef.
/// </summary>
public static class CompleteChordRounds
{
    /// <summary>A chord a round may play: its root, its quality and its notes, from the root up.</summary>
    public sealed record Candidate(string Root, string Quality, IReadOnlyList<string> Notes);

    private static readonly string[] Triads = ["major", "minor", "diminished", "augmented"];
    private static readonly string[] Sevenths = ["major7", "dominant7", "minor7", "halfDiminished", "diminished7"];

    // The roots of each quality, spelled so that every note of the chord takes at most one
    // accidental, which the staff editor can write. B augmented (B D# F##) has none.
    private static readonly Dictionary<string, string[]> Roots = new()
    {
        ["major"] = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"],
        ["minor"] = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B"],
        ["diminished"] = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"],
        ["augmented"] = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb"],
        ["major7"] = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"],
        ["dominant7"] = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"],
        ["minor7"] = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B"],
        ["halfDiminished"] = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"],
        ["diminished7"] = ["C#", "D", "D#", "E", "F#", "G", "G#", "A", "A#", "B"],
    };

    /// <summary>
    /// The qualities of a <c>ccQuality</c> value: major, minor, both, triads (with the
    /// diminished and augmented ones), sevenths, or all of them. Major for any other value.
    /// </summary>
    public static IReadOnlyList<string> Qualities(string? filter) => filter switch
    {
        "minor" => ["minor"],
        "both" => ["major", "minor"],
        "triads" => Triads,
        "sevenths" => Sevenths,
        "all" => [.. Triads, .. Sevenths],
        _ => ["major"],
    };

    /// <summary>
    /// The chords a round may play, in <paramref name="octave"/>: those of the qualities of
    /// <paramref name="quality"/> (<see cref="Qualities"/>), on every root, and only those
    /// written without accidentals unless <paramref name="accidentals"/> is <c>any</c>.
    /// </summary>
    public static IReadOnlyList<Candidate> Candidates(string? quality, string? accidentals, int octave) =>
    [
        .. from q in Qualities(quality)
           from root in Roots[q]
           let notes = MusicTheoryService.GetChordNotes(root + octave, q)
           where accidentals == "any" || notes.All(IsNatural)
           select new Candidate(root, q, notes)
    ];

    /// <summary>
    /// A round: one of the <see cref="Candidates"/>, each as likely. Its answer is the notes
    /// the student writes, from the bottom up: all of them, or those above the root when the
    /// root is given on the staff (<c>promptNotes</c>). The editor offers <c>slots</c> notes,
    /// enough for the largest chord of the filters, so it doesn't tell a triad from a seventh.
    /// </summary>
    public static object Generate(IReadOnlyDictionary<string, string> filters, Random random)
    {
        var octave = filters.GetValueOrDefault("ccOctave") == "3" ? 3 : 4;
        var quality = filters.GetValueOrDefault("ccQuality");
        var candidates = Candidates(quality, filters.GetValueOrDefault("ccAccidentals"), octave);
        var rootGiven = filters.GetValueOrDefault("ccRoot") != "hidden";
        var chord = candidates[random.Next(candidates.Count)];
        var written = rootGiven ? chord.Notes.Skip(1) : chord.Notes;

        return new
        {
            chordRoot = chord.Root,
            chordQuality = chord.Quality,
            octave,
            clef = octave == 3 ? "bass" : "treble",
            slots = candidates.Max(c => c.Notes.Count) - (rootGiven ? 1 : 0),
            chordNotes = chord.Notes,
            promptNotes = rootGiven ? new[] { chord.Notes[0] } : [],
            answerString = string.Join("|", written.Select(note => $"{note}:w")),
        };
    }

    private static bool IsNatural(string note) => Regex.IsMatch(note, @"^[A-G]-?\d+$");
}
