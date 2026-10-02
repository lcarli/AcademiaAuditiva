using System.Collections.Concurrent;
using System.Globalization;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>A chord shape on the neck of a guitar in standard tuning.</summary>
public sealed class GuitarShape
{
    /// <summary>MIDI notes of the open strings, from the low E (E2) to the high E (E4).</summary>
    public static IReadOnlyList<int> OpenStrings { get; } = [40, 45, 50, 55, 59, 64];

    /// <param name="frets">
    /// The fret of each string, from the low E to the high E: 0 for an open string,
    /// <c>null</c> for a string that is not played.
    /// </param>
    public GuitarShape(IReadOnlyList<int?> frets)
    {
        ArgumentNullException.ThrowIfNull(frets);
        if (frets.Count != OpenStrings.Count)
            throw new ArgumentException($"A shape has a fret for each of the {OpenStrings.Count} strings.", nameof(frets));

        Frets = frets;
        Notes = [.. frets.Select((fret, s) => OpenStrings[s] + fret).OfType<int>()];
    }

    /// <summary>The fret of each string, from the low E to the high E (<c>null</c>: not played).</summary>
    public IReadOnlyList<int?> Frets { get; }

    /// <summary>MIDI notes of the strings that are played, from the low string up, as a downstroke strums them.</summary>
    public IReadOnlyList<int> Notes { get; }

    /// <summary>The shape as chord charts write it, from the low E: <c>x32010</c> is C major.</summary>
    public override string ToString() => string.Concat(Frets.Select(fret => fret switch
    {
        null => "x",
        < 10 => fret.Value.ToString(CultureInfo.InvariantCulture),
        _ => "(" + fret.Value.ToString(CultureInfo.InvariantCulture) + ")",
    }));
}

/// <summary>Where on the neck the guitar plays the chords of an exercise.</summary>
public enum GuitarPosition
{
    /// <summary>
    /// The chords of the first frets, as a guitarist learns them first (the default): open
    /// chords, where open strings ring, and barre chords for the others. C is <c>x32010</c>,
    /// F <c>133211</c>.
    /// </summary>
    Open,

    /// <summary>
    /// Barre chords: every string that is played is fretted, as near the nut as the chord
    /// allows. C is <c>x35553</c>, G <c>355433</c>.
    /// </summary>
    Barre,

    /// <summary>
    /// High on the neck, from the 7th fret up, on the highest strings. C is <c>xx(10)988</c>,
    /// G <c>x(10)(12)(12)(12)(10)</c>. The few chords a hand can't hold up there are played as
    /// near the 7th fret as it can.
    /// </summary>
    High,
}

/// <summary>
/// Plays a chord the way a guitarist does: rather than the close stack of notes the piano
/// plays, a shape on the neck that strums four to six strings, doubling notes in the
/// octaves the strings give them. C major is <c>x32010</c> (C3 E3 G3 C4 E4), F major the
/// barre chord <c>133211</c>.
/// </summary>
/// <remarks>
/// Every shape in the <see cref="GuitarPosition"/> asked for that sounds the chord and that a
/// hand can hold is tried, and the easiest one wins: near the nut, with few fingers, a small
/// stretch, and as many strings as the bass allows (high on the neck, the highest strings
/// are enough). The open chords also keep the bass in octave 2, as low as the neck has it.
/// Those costs make the common open and barre chords come out (see <c>GuitarVoicingTests</c>).
/// </remarks>
public static class GuitarVoicing
{
    private const int HighestFret = 12;

    // A strum sounds a run of at least four strings, all of them notes of the chord.
    private const int FewestStrings = 4;

    // The index finger can lie across the strings (a barre); the other fingers fret one string each.
    private const int Fingers = 4;
    private const int WidestStretch = 3;

    // Open strings ring along only with chords played near the nut.
    private const int HighestFretWithOpenStrings = 4;

    // The basses of the open chords: from the low E string (E2), or in octave 3 for the notes
    // the neck has no lower (C3 to D#3).
    private const int OpenChordsBassOctave = 2;

    // The position high on the neck starts at the 7th fret.
    private const int HighPositionFret = 7;

    // Costs of what makes a shape harder to play or thinner to hear; each finger costs 1.
    private const double PositionCost = 0.5;
    private const double LowStringNotPlayedCost = 1.5;
    private const double HighStringNotPlayedCost = 3.0;
    private const double FourFingersWithoutBarreCost = 1.5;
    private const double StretchCost = 0.5;

    // The pitch classes of a chord, the pitch class of its bass, and the position on the neck.
    private static readonly ConcurrentDictionary<(int PitchClasses, int Bass, GuitarPosition Position), GuitarShape?> Shapes = new();

    /// <summary>
    /// The easiest shape in <paramref name="position"/> that plays the notes of
    /// <paramref name="midiNotes"/>, in any octave, with the lowest of them in the bass (so an
    /// inversion stays one), or <c>null</c> when no shape plays them. A chord the position has
    /// no shape for is played on the open chords.
    /// </summary>
    public static GuitarShape? Find(IEnumerable<int> midiNotes, GuitarPosition position = GuitarPosition.Open)
    {
        ArgumentNullException.ThrowIfNull(midiNotes);

        var pitchClasses = 0;
        var lowest = int.MaxValue;
        foreach (var note in midiNotes)
        {
            pitchClasses |= 1 << PitchClass(note);
            lowest = Math.Min(lowest, note);
        }
        if (pitchClasses == 0)
            return null;

        return Shapes.GetOrAdd((pitchClasses, PitchClass(lowest), position), key =>
            Search(key.PitchClasses, key.Bass, key.Position)
            ?? (key.Position == GuitarPosition.Open ? null : Search(key.PitchClasses, key.Bass, GuitarPosition.Open)));
    }

    /// <summary>
    /// The position called <paramref name="name"/> in any case (it comes from a cookie or a
    /// request), or <see cref="GuitarPosition.Open"/> when there is no such position.
    /// </summary>
    public static GuitarPosition PositionFromName(string? name)
    {
        foreach (var position in Enum.GetValues<GuitarPosition>())
        {
            if (string.Equals(position.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                return position;
        }
        return GuitarPosition.Open;
    }

    private static int PitchClass(int midi) => (midi % 12 + 12) % 12;

    private static GuitarShape? Search(int pitchClasses, int bass, GuitarPosition position)
    {
        switch (position)
        {
            case GuitarPosition.Open:
                return Search(pitchClasses, bass, 0, (OpenChordsBassOctave + 1) * 12 + bass, LowStringNotPlayedCost);

            // Barre chords fret every string they play.
            case GuitarPosition.Barre:
                return Search(pitchClasses, bass, 1, null, LowStringNotPlayedCost);

            // High on the neck, the highest strings are enough; a chord a hand can't hold from
            // the 7th fret up comes down a fret at a time.
            default:
                for (var lowestFret = HighPositionFret; lowestFret >= 1; lowestFret--)
                {
                    if (Search(pitchClasses, bass, lowestFret, null, 0.0) is { } shape)
                        return shape;
                }
                return null;
        }
    }

    /// <summary>
    /// The easiest shape that plays <paramref name="pitchClasses"/> with <paramref name="bass"/>
    /// in the bass, fretting no string below <paramref name="lowestFret"/> (0 lets the open
    /// strings ring), with the bass nearest <paramref name="bassNote"/> when there is one.
    /// </summary>
    private static GuitarShape? Search(int pitchClasses, int bass, int lowestFret, int? bassNote, double lowStringNotPlayedCost)
    {
        var strings = GuitarShape.OpenStrings.Count;

        // What each string can do: not be played, or play a note of the chord.
        var choices = new List<int?>[strings];
        for (var s = 0; s < strings; s++)
        {
            choices[s] = [null];
            for (var fret = lowestFret; fret <= HighestFret; fret++)
            {
                if ((pitchClasses & 1 << PitchClass(GuitarShape.OpenStrings[s] + fret)) != 0)
                    choices[s].Add(fret);
            }
        }

        var frets = new int?[strings];
        int?[]? best = null;
        var bestScore = (Distance: int.MaxValue, Cost: double.MaxValue, Position: 0, FretSum: 0);
        Try(0);
        return best is null ? null : new GuitarShape(best);

        void Try(int s)
        {
            if (s == strings)
            {
                // The nearest bass wins, then the easiest shape; ties go to the shape
                // nearer the nut, then to the one found first.
                if (Score(frets, pitchClasses, bass, bassNote, lowStringNotPlayedCost) is { } score
                    && score.CompareTo(bestScore) < 0)
                {
                    bestScore = score;
                    best = (int?[])frets.Clone();
                }
                return;
            }
            foreach (var fret in choices[s])
            {
                frets[s] = fret;
                Try(s + 1);
            }
        }
    }

    /// <summary>
    /// How far the bass of <paramref name="frets"/> is from <paramref name="bassNote"/> (0 when
    /// any octave will do) and how hard the shape is to play, or <c>null</c> when it can't be
    /// strummed as the chord, with the pitch class <paramref name="bass"/> in the bass, or held
    /// by one hand.
    /// </summary>
    private static (int Distance, double Cost, int Position, int FretSum)? Score(
        int?[] frets, int pitchClasses, int bass, int? bassNote, double lowStringNotPlayedCost)
    {
        var first = Array.FindIndex(frets, fret => fret is not null);
        var last = Array.FindLastIndex(frets, fret => fret is not null);
        if (first < 0 || last - first + 1 < FewestStrings)
            return null;

        Span<int> notes = stackalloc int[frets.Length];
        var count = 0;
        var sounded = 0;
        var lowestNote = int.MaxValue;
        var lowestFret = int.MaxValue;
        var highestFret = 0;
        var fretSum = 0;
        var openStrings = false;
        for (var s = first; s <= last; s++)
        {
            // The strum crosses every string between the lowest and the highest one played.
            if (frets[s] is not { } fret)
                return null;

            // Two strings on the same note would blur the sound rather than fill it.
            var note = GuitarShape.OpenStrings[s] + fret;
            if (notes[..count].Contains(note))
                return null;
            notes[count++] = note;

            sounded |= 1 << PitchClass(note);
            lowestNote = Math.Min(lowestNote, note);
            fretSum += fret;
            if (fret == 0)
            {
                openStrings = true;
            }
            else
            {
                lowestFret = Math.Min(lowestFret, fret);
                highestFret = Math.Max(highestFret, fret);
            }
        }

        if (sounded != pitchClasses || PitchClass(lowestNote) != bass)
            return null;

        var position = highestFret == 0 ? 0 : lowestFret;
        var stretch = highestFret - position;
        if (stretch > WidestStretch || (openStrings && highestFret > HighestFretWithOpenStrings))
            return null;

        var (fingers, barre) = Fingering(frets);
        if (fingers > Fingers)
            return null;

        var cost = PositionCost * position
            + lowStringNotPlayedCost * first
            + HighStringNotPlayedCost * (frets.Length - 1 - last)
            + fingers
            + (fingers == Fingers && !barre ? FourFingersWithoutBarreCost : 0)
            + StretchCost * stretch * stretch;
        return (bassNote is { } target ? Math.Abs(lowestNote - target) : 0, cost, position, fretSum);
    }

    /// <summary>
    /// The fingers that fret <paramref name="frets"/>: one per fretted string, or a barre of the
    /// index finger across the lowest fret (with no open string under it) plus one finger per
    /// string fretted higher, when that takes fewer.
    /// </summary>
    private static (int Fingers, bool Barre) Fingering(int?[] frets)
    {
        var fretted = 0;
        var lowest = int.MaxValue;
        foreach (var fret in frets)
        {
            if (fret is > 0)
            {
                fretted++;
                lowest = Math.Min(lowest, fret.Value);
            }
        }

        var first = Array.IndexOf(frets, lowest);
        var last = Array.LastIndexOf(frets, lowest);
        if (fretted == 0 || first == last)
            return (fretted, false);

        for (var s = first; s <= last; s++)
        {
            if (frets[s] is not { } fret || fret < lowest)
                return (fretted, false);
        }

        var withBarre = 1 + frets.Count(fret => fret > lowest);
        return withBarre < fretted ? (withBarre, true) : (fretted, false);
    }
}
