namespace AcademiaAuditiva.Models;

/// <summary>
/// A sound picked on the Explore page. Only the fields of the chosen
/// <see cref="Kind"/> are read; <c>ExploreSoundBuilder</c> checks every
/// value against its lists.
/// </summary>
public sealed class ExplorePlayRequest
{
    /// <summary><c>note</c>, <c>interval</c>, <c>chord</c> or <c>scale</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>Pitch class of the root, named with sharps (<c>C</c> … <c>B</c>).</summary>
    public string? Root { get; set; }

    /// <summary>Octave of the root (C4 is middle C).</summary>
    public int Octave { get; set; }

    /// <summary>Interval code, as in the <c>Exercise.Interval.*</c> texts (<c>3min</c>, <c>5J</c> …).</summary>
    public string? Interval { get; set; }

    /// <summary><c>ascending</c>, <c>descending</c> or, for intervals, <c>harmonic</c>.</summary>
    public string? Direction { get; set; }

    /// <summary>Chord quality (<c>major</c>, <c>dominant7</c> …).</summary>
    public string? Quality { get; set; }

    /// <summary>How many of the lowest chord tones move up an octave.</summary>
    public int Inversion { get; set; }

    /// <summary>Plays the chord one note at a time instead of together.</summary>
    public bool Arpeggio { get; set; }

    /// <summary>Scale or mode (<c>major</c>, <c>dorian</c> …).</summary>
    public string? Scale { get; set; }
}
