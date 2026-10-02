using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// One tone of an interval, chord or scale: how far it sits from the root in
/// semitones and in letter names, so it is spelled on the right letter
/// (a minor third above C is Eb, not D#).
/// </summary>
public readonly record struct Tone(int Semitones, int LetterSteps);

/// <summary>
/// A sound picked on the Explore page: its spelled root (no octave), its
/// notes in playing order (low to high when they sound together) and the
/// mixer plan that plays them.
/// </summary>
public sealed record ExploreSound(
    string Root,
    IReadOnlyList<string> Notes,
    bool Simultaneous,
    IReadOnlyList<MixInput> Plan);

/// <summary>
/// Turns an Explore page choice (a note, interval, chord or scale on a
/// root) into spelled notes and a mixer plan. Only the choices listed here
/// are accepted, which keeps every note inside the piano samples and the
/// number of distinct mixes small. Nothing is scored, so nothing is hidden:
/// the learner hears and sees exactly what they picked.
/// </summary>
public sealed class ExploreSoundBuilder
{
    public const string NoteKind = "note";
    public const string IntervalKind = "interval";
    public const string ChordKind = "chord";
    public const string ScaleKind = "scale";

    public const string Ascending = "ascending";
    public const string Descending = "descending";
    public const string Harmonic = "harmonic";

    private const double NoteSeconds = 2.0;
    private const double IntervalFirstNoteSeconds = 1.5;
    private const double IntervalSecondNoteStartSeconds = 1.75;
    private const double ArpeggioStepSeconds = 0.4;
    private const double ScaleNoteSeconds = 0.75;
    private const double ScaleNoteGapSeconds = 0.1;
    private const double ScaleLastNoteSeconds = 1.5;

    /// <summary>The twelve roots, one per piano key, named with sharps.</summary>
    public static IReadOnlyList<string> Roots { get; } =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly Dictionary<string, string> FlatTwins = new()
    {
        ["C#"] = "Db",
        ["D#"] = "Eb",
        ["F#"] = "Gb",
        ["G#"] = "Ab",
        ["A#"] = "Bb",
    };

    private static readonly Dictionary<string, (int Min, int Max)> Octaves = new()
    {
        [NoteKind] = (1, 7),
        [IntervalKind] = (2, 6),
        [ChordKind] = (2, 5),
        [ScaleKind] = (2, 6),
    };

    private static readonly (string Code, Tone Tone)[] IntervalTable =
    [
        ("2min", new(1, 1)),
        ("2maj", new(2, 1)),
        ("3min", new(3, 2)),
        ("3maj", new(4, 2)),
        ("4J", new(5, 3)),
        ("4A", new(6, 3)),
        ("5dim", new(6, 4)),
        ("5J", new(7, 4)),
        ("6min", new(8, 5)),
        ("6maj", new(9, 5)),
        ("7min", new(10, 6)),
        ("7maj", new(11, 6)),
        ("8J", new(12, 7)),
    ];

    private static readonly (string Code, Tone[] Tones)[] ChordTable =
    [
        ("major", [new(0, 0), new(4, 2), new(7, 4)]),
        ("minor", [new(0, 0), new(3, 2), new(7, 4)]),
        ("diminished", [new(0, 0), new(3, 2), new(6, 4)]),
        ("augmented", [new(0, 0), new(4, 2), new(8, 4)]),
        ("sus2", [new(0, 0), new(2, 1), new(7, 4)]),
        ("sus4", [new(0, 0), new(5, 3), new(7, 4)]),
        ("major7", [new(0, 0), new(4, 2), new(7, 4), new(11, 6)]),
        ("dominant7", [new(0, 0), new(4, 2), new(7, 4), new(10, 6)]),
        ("minor7", [new(0, 0), new(3, 2), new(7, 4), new(10, 6)]),
        ("halfDiminished", [new(0, 0), new(3, 2), new(6, 4), new(10, 6)]),
        ("diminished7", [new(0, 0), new(3, 2), new(6, 4), new(9, 6)]),
    ];

    private static readonly (string Code, Tone[] Tones)[] ScaleTable =
    [
        ("major", Heptatonic(0, 2, 4, 5, 7, 9, 11)),
        ("minor", Heptatonic(0, 2, 3, 5, 7, 8, 10)),
        ("majorPentatonic", [new(0, 0), new(2, 1), new(4, 2), new(7, 4), new(9, 5), new(12, 7)]),
        ("minorPentatonic", [new(0, 0), new(3, 2), new(5, 3), new(7, 4), new(10, 6), new(12, 7)]),
    ];

    private static readonly (string Code, Tone[] Tones)[] ModeTable =
    [
        ("ionian", Heptatonic(0, 2, 4, 5, 7, 9, 11)),
        ("dorian", Heptatonic(0, 2, 3, 5, 7, 9, 10)),
        ("phrygian", Heptatonic(0, 1, 3, 5, 7, 8, 10)),
        ("lydian", Heptatonic(0, 2, 4, 6, 7, 9, 11)),
        ("mixolydian", Heptatonic(0, 2, 4, 5, 7, 9, 10)),
        ("aeolian", Heptatonic(0, 2, 3, 5, 7, 8, 10)),
        ("locrian", Heptatonic(0, 1, 3, 5, 6, 8, 10)),
    ];

    private static readonly Dictionary<string, Tone> IntervalTones = IntervalTable.ToDictionary(i => i.Code, i => i.Tone);
    private static readonly Dictionary<string, Tone[]> ChordTones = ChordTable.ToDictionary(c => c.Code, c => c.Tones);
    private static readonly Dictionary<string, Tone[]> ScaleTones =
        ScaleTable.Concat(ModeTable).ToDictionary(s => s.Code, s => s.Tones);

    /// <summary>Interval codes, smallest first; they match the <c>Exercise.Interval.*</c> texts.</summary>
    public static IEnumerable<string> IntervalCodes => IntervalTable.Select(i => i.Code);

    /// <summary>Chord qualities with their number of tones (3 or 4), in menu order.</summary>
    public static IEnumerable<(string Code, int ToneCount)> ChordQualities =>
        ChordTable.Select(c => (c.Code, c.Tones.Length));

    /// <summary>Scales in menu order (major, minor, pentatonics).</summary>
    public static IEnumerable<string> ScaleNames => ScaleTable.Select(s => s.Code);

    /// <summary>The seven modes of the major scale, from Ionian to Locrian.</summary>
    public static IEnumerable<string> ModeNames => ModeTable.Select(m => m.Code);

    /// <summary>Octaves a root of <paramref name="kind"/> may sit in, so every note has a sample.</summary>
    public static (int Min, int Max) OctavesFor(string kind) => Octaves[kind];

    /// <summary>
    /// Builds the sound for <paramref name="request"/>, or returns <c>null</c>
    /// when anything in it is not on the lists.
    /// </summary>
    public ExploreSound? Build(ExplorePlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind is null
            || !Octaves.TryGetValue(request.Kind, out var octaves)
            || request.Octave < octaves.Min
            || request.Octave > octaves.Max
            || request.Root is null
            || !Roots.Contains(request.Root))
        {
            return null;
        }

        var sound = request.Kind switch
        {
            NoteKind => Note(request.Root, request.Octave),
            IntervalKind => Interval(request),
            ChordKind => Chord(request),
            ScaleKind => Scale(request),
            _ => null,
        };

        return sound is not null && sound.Notes.All(n => MusicTheoryService.NoteToMidi(n) is { } midi && PianoSamples.Covers(midi))
            ? sound
            : null;
    }

    private static ExploreSound Note(string root, int octave)
    {
        var note = root + octave;
        return new ExploreSound(root, [note], Simultaneous: false, [new MixInput(Blob(note), 0, NoteSeconds)]);
    }

    private static ExploreSound? Interval(ExplorePlayRequest request)
    {
        if (request.Interval is null
            || !IntervalTones.TryGetValue(request.Interval, out var tone)
            || request.Direction is not (Ascending or Descending or Harmonic))
        {
            return null;
        }

        var sign = request.Direction == Descending ? -1 : 1;
        var (root, notes) = Spell(request.Root!, request.Octave,
            [new Tone(0, 0), new Tone(sign * tone.Semitones, sign * tone.LetterSteps)]);

        if (request.Direction == Harmonic)
        {
            return new ExploreSound(root, notes, Simultaneous: true, Together(notes));
        }

        return new ExploreSound(root, notes, Simultaneous: false,
        [
            new MixInput(Blob(notes[0]), 0, IntervalFirstNoteSeconds),
            new MixInput(Blob(notes[1]), IntervalSecondNoteStartSeconds, NoteSeconds),
        ]);
    }

    private static ExploreSound? Chord(ExplorePlayRequest request)
    {
        if (request.Quality is null
            || !ChordTones.TryGetValue(request.Quality, out var tones)
            || request.Inversion < 0
            || request.Inversion >= tones.Length)
        {
            return null;
        }

        var (root, rootPosition) = Spell(request.Root!, request.Octave, tones);

        // An inversion moves the lowest tones up an octave; they keep their names.
        var notes = rootPosition.Skip(request.Inversion)
            .Concat(rootPosition.Take(request.Inversion).Select(n => MusicTheoryService.SpellInterval(n, 12, 7)!))
            .ToArray();

        if (!request.Arpeggio)
        {
            return new ExploreSound(root, notes, Simultaneous: true, Together(notes));
        }

        // Each note rings until the last one fades, like a held pedal.
        var end = ArpeggioStepSeconds * (notes.Length - 1) + NoteSeconds;
        var plan = notes
            .Select((n, i) => new MixInput(Blob(n), i * ArpeggioStepSeconds, end - i * ArpeggioStepSeconds))
            .ToArray();
        return new ExploreSound(root, notes, Simultaneous: false, plan);
    }

    private static ExploreSound? Scale(ExplorePlayRequest request)
    {
        if (request.Scale is null
            || !ScaleTones.TryGetValue(request.Scale, out var tones)
            || request.Direction is not (Ascending or Descending))
        {
            return null;
        }

        var (root, notes) = Spell(request.Root!, request.Octave, tones);
        if (request.Direction == Descending)
        {
            Array.Reverse(notes);
        }

        const double step = ScaleNoteSeconds + ScaleNoteGapSeconds;
        var plan = notes
            .Select((n, i) => new MixInput(Blob(n), i * step, i == notes.Length - 1 ? ScaleLastNoteSeconds : ScaleNoteSeconds))
            .ToArray();
        return new ExploreSound(root, notes, Simultaneous: false, plan);
    }

    // A black-key root can be named with a sharp or a flat. Keep the name whose
    // notes read most simply (C# minor but Db major); the sharp wins a tie.
    private static (string Root, string[] Notes) Spell(string root, int octave, IReadOnlyList<Tone> tones)
    {
        var notes = SpellFrom(root, octave, tones);
        if (FlatTwins.TryGetValue(root, out var flat))
        {
            var flatNotes = SpellFrom(flat, octave, tones);
            if (Awkwardness(flatNotes) < Awkwardness(notes))
            {
                return (flat, flatNotes);
            }
        }

        return (root, notes);
    }

    private static string[] SpellFrom(string root, int octave, IReadOnlyList<Tone> tones)
    {
        var rootNote = root + octave;
        return tones.Select(t => MusicTheoryService.SpellInterval(rootNote, t.Semitones, t.LetterSteps)!).ToArray();
    }

    // Naturals read best, then sharps and flats, then sharps and flats on
    // white keys (E#, B#, Fb, Cb), then double sharps and flats.
    private static int Awkwardness(IEnumerable<string> notes) => notes.Sum(note =>
    {
        var name = note.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return name switch
        {
            { Length: 1 } => 0,
            "E#" or "B#" or "Fb" or "Cb" => 2,
            { Length: 2 } => 1,
            _ => 4,
        };
    });

    private static MixInput[] Together(IEnumerable<string> notes) =>
        notes.Select(n => new MixInput(Blob(n), 0, NoteSeconds)).ToArray();

    private static string Blob(string note) => PianoSamples.BlobName(MusicTheoryService.NoteToMidi(note)!.Value);

    private static Tone[] Heptatonic(params int[] semitones) =>
        [.. semitones.Select((s, i) => new Tone(s, i)), new Tone(12, 7)];
}
