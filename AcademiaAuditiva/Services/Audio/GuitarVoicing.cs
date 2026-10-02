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

/// <summary>
/// Plays a chord the way a guitarist does: rather than the close stack of notes the piano
/// plays, a shape on the neck that strums four to six strings, doubling notes in the
/// octaves the strings give them. C major is <c>x32010</c> (C3 E3 G3 C4 E4), F major the
/// barre chord <c>133211</c>.
/// </summary>
/// <remarks>
/// Every shape that sounds the chord and that a hand can hold is tried, and the easiest wins:
/// near the nut, with few fingers, a small stretch, and as many strings as the bass allows.
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

    // Costs of what makes a shape harder to play or thinner to hear; each finger costs 1.
    private const double PositionCost = 0.5;
    private const double LowStringNotPlayedCost = 1.5;
    private const double HighStringNotPlayedCost = 3.0;
    private const double FourFingersWithoutBarreCost = 1.5;
    private const double StretchCost = 0.5;

    private static readonly ConcurrentDictionary<(int PitchClasses, int Bass), GuitarShape?> Shapes = new();

    /// <summary>
    /// The easiest shape that plays the notes of <paramref name="midiNotes"/>, in any octave,
    /// with the lowest of them in the bass (so an inversion stays one), or <c>null</c> when no
    /// shape plays them.
    /// </summary>
    public static GuitarShape? Find(IEnumerable<int> midiNotes)
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

        return Shapes.GetOrAdd((pitchClasses, PitchClass(lowest)), key => Search(key.PitchClasses, key.Bass));
    }

    private static int PitchClass(int midi) => (midi % 12 + 12) % 12;

    private static GuitarShape? Search(int pitchClasses, int bass)
    {
        var strings = GuitarShape.OpenStrings.Count;

        // What each string can do: not be played, or play a note of the chord.
        var choices = new List<int?>[strings];
        for (var s = 0; s < strings; s++)
        {
            choices[s] = [null];
            for (var fret = 0; fret <= HighestFret; fret++)
            {
                if ((pitchClasses & 1 << PitchClass(GuitarShape.OpenStrings[s] + fret)) != 0)
                    choices[s].Add(fret);
            }
        }

        var frets = new int?[strings];
        int?[]? best = null;
        var bestScore = (Cost: double.MaxValue, Position: 0, FretSum: 0);
        Try(0);
        return best is null ? null : new GuitarShape(best);

        void Try(int s)
        {
            if (s == strings)
            {
                // Ties go to the shape nearer the nut, then to the one found first.
                if (Score(frets, pitchClasses, bass) is { } score && score.CompareTo(bestScore) < 0)
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
    /// How hard <paramref name="frets"/> is to play, or <c>null</c> when it can't be strummed
    /// as the chord or held by one hand.
    /// </summary>
    private static (double Cost, int Position, int FretSum)? Score(int?[] frets, int pitchClasses, int bass)
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
            + LowStringNotPlayedCost * first
            + HighStringNotPlayedCost * (frets.Length - 1 - last)
            + fingers
            + (fingers == Fingers && !barre ? FourFingersWithoutBarreCost : 0)
            + StretchCost * stretch * stretch;
        return (cost, position, fretSum);
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
