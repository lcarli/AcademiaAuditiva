using System.Globalization;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services;

/// <summary>
/// RhythmTap: the student hears a rhythm of <see cref="Measures"/> bars at a level of
/// RhythmDictation (<see cref="Draw"/>) and taps it back. The page sends when each tap came, in
/// milliseconds (<c>0,498,1003</c>), and the taps play the rhythm when there is one for each note
/// and each is where its note starts (<see cref="Check"/>). The student may start whenever they
/// like, and tap a little faster or slower than the rhythm was played (<see cref="MostTempoChange"/>):
/// the notes are matched to the taps at the tempo and from the moment that fit them best.
/// </summary>
public static class RhythmTaps
{
    /// <summary>How many bars a rhythm lasts.</summary>
    public const int Measures = 2;

    /// <summary>
    /// The fewest notes a rhythm has: the tempo and the moment the notes are matched at would
    /// make almost any two taps fit two notes.
    /// </summary>
    public const int FewestNotes = 3;

    /// <summary>The most taps a guess may have: a rhythm has at most 32 notes, two bars of sixteenths.</summary>
    public const int MostTaps = 64;

    /// <summary>How much faster or slower than the rhythm was played it may be tapped: 15%.</summary>
    public const double MostTempoChange = 0.15;

    /// <summary>The furthest a tap may be from its note, in milliseconds…</summary>
    public const int MostOff = 100;

    /// <summary>…or this share of the shortest time between two notes, when that is less.</summary>
    public const double ShareOff = 0.4;

    // Rhythms drawn before giving up, and the longest guess read: MostTaps of six digits and a comma.
    private const int Attempts = 1000;
    private const int LongestGuess = MostTaps * 7;

    /// <summary>
    /// How the taps of a guess went, sent to the page with the result (<c>detail</c>): how many
    /// notes the rhythm has and how many taps came; when there was a tap for each note, how far
    /// each was from its note in milliseconds, early ones below zero (null otherwise); and how
    /// far a tap may be from its note.
    /// </summary>
    public sealed record Result(int Notes, int Taps, IReadOnlyList<int>? Deviations, int Tolerance)
    {
        /// <summary>Whether the taps play the rhythm: one for each note, none further from it than <see cref="Tolerance"/>.</summary>
        public bool InTime() => Deviations is { } deviations && deviations.All(off => Math.Abs(off) <= Tolerance);
    }

    /// <summary>
    /// A rhythm at the <c>rdLevel</c> value <paramref name="level"/> (<see cref="DictationRhythm.IsLevel"/>),
    /// drawn as a dictation of <see cref="Measures"/> bars is, in one of the level's time signatures.
    /// It starts with a note on its first beat, every bar has a note, and it has
    /// <see cref="FewestNotes"/> at least.
    /// </summary>
    public static (string TimeSignature, IReadOnlyList<IReadOnlyList<string>> Bars) Draw(int level, Random random)
    {
        var taught = DictationRhythm.Find(level);
        var plain = DictationRhythm.FindRandom(level);
        if (taught is null && plain is null)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Not a level of RhythmDictation.");
        var timeSignatures = taught?.TimeSignatures ?? plain!.TimeSignatures;
        var timeSignature = timeSignatures[random.Next(timeSignatures.Count)];

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var bars = taught is not null
                ? DictationRhythm.Bars(taught, timeSignature, Measures, random, melodic: true)
                : DictationRhythm.RandomBars(plain!.Durations, plain.RestChance, timeSignature, Measures, random, melodic: true);
            if (bars.All(bar => bar.Any(IsNote)) && bars.Sum(bar => bar.Count(IsNote)) >= FewestNotes)
                return (timeSignature, bars);
        }

        throw new InvalidOperationException($"No rhythm of level {level} in {timeSignature} has {FewestNotes} notes.");
    }

    /// <summary>
    /// When the page starts taking the taps of a round, in seconds from the start of its clip,
    /// at <paramref name="tempo"/> quarter notes a minute. The clip plays the count-in
    /// (<see cref="DictationRhythm.CountIn"/>), the <see cref="Measures"/> bars of the rhythm in
    /// <paramref name="timeSignature"/>, and the count-in again, and the first tap is due where
    /// the next bar would start. Taps count from halfway between the last click and then: a
    /// first tap may come a little early, and taps along with the clicks are not counted.
    /// </summary>
    public static double TapsFrom(string timeSignature, int tempo)
    {
        var clicks = DictationRhythm.CountIn(timeSignature);
        var countIn = clicks.Sum(click => click.Beats);
        var bars = Measures * DictationRhythm.BarLength(timeSignature) / 4.0;
        var beats = 2 * countIn + bars - clicks[^1].Beats / 2;
        return Math.Round(beats * 60.0 / tempo, 3);
    }

    /// <summary>
    /// When each note of a round's rhythm starts, in milliseconds from its first beat: its
    /// <c>melody</c> as the planner plays it, at its <c>tempo</c>. Rests are not tapped.
    /// </summary>
    public static IReadOnlyList<double> Onsets(JObject round)
    {
        var beatMilliseconds = 60000.0 / (round.Value<int?>("tempo") ?? DictationRhythm.Tempos[0]);
        var onsets = new List<double>();
        var beats = 0.0;
        foreach (var entry in round["melody"] as JArray ?? new JArray())
        {
            if (entry.Value<string>("type") == "note") onsets.Add(beats * beatMilliseconds);
            beats += entry.Value<double?>("durationBeats") ?? 0.0;
        }
        return onsets;
    }

    /// <summary>
    /// The taps of a guess: whole milliseconds in order, separated by commas, none for an empty
    /// guess. Null when the guess is no such list or has more than <see cref="MostTaps"/>.
    /// </summary>
    public static IReadOnlyList<double>? Parse(string? guess)
    {
        if (string.IsNullOrWhiteSpace(guess)) return [];
        if (guess.Length > LongestGuess) return null;

        var taps = new List<double>();
        foreach (var part in guess.Split(','))
        {
            if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                || (taps.Count > 0 && milliseconds < taps[^1]))
                return null;
            taps.Add(milliseconds);
        }
        return taps.Count <= MostTaps ? taps : null;
    }

    /// <summary>
    /// How <paramref name="taps"/> play the notes starting at <paramref name="onsets"/>, both in
    /// milliseconds. With a tap for each note, the notes are matched to the taps by least squares,
    /// at the tempo (within <see cref="MostTempoChange"/> of the one played) and from the moment
    /// that fit them best, and each tap is as far from its note as from where that puts it. A tap
    /// may be <see cref="MostOff"/> from its note, or <see cref="ShareOff"/> of the shortest time
    /// between two notes when that is less, so it is nearer its note than the next one. One tap
    /// far off pulls that match with it, and with it taps that were on their notes: when some are
    /// off, the notes are matched to the taps near the line through two of them instead, the two
    /// that leave the fewest off.
    /// </summary>
    public static Result Check(IReadOnlyList<double> onsets, IReadOnlyList<double> taps)
    {
        var shortest = onsets.Zip(onsets.Skip(1), (onset, next) => next - onset).DefaultIfEmpty(double.PositiveInfinity).Min();
        var tolerance = (int)Math.Min(MostOff, Math.Floor(ShareOff * shortest));
        if (onsets.Count == 0 || taps.Count != onsets.Count)
            return new Result(onsets.Count, taps.Count, null, tolerance);

        var every = Enumerable.Range(0, onsets.Count).ToList();
        var best = Deviations(onsets, taps, every);
        for (var first = 0; first < onsets.Count && Off(best, tolerance) > 0; first++)
        {
            for (var second = first + 1; second < onsets.Count; second++)
            {
                var through = Deviations(onsets, taps, [first, second]);
                var near = every.Where(i => Math.Abs(through[i]) <= tolerance).ToList();
                if (near.Count == 0) continue;
                var match = Deviations(onsets, taps, near);
                var (off, bestOff) = (Off(match, tolerance), Off(best, tolerance));
                if (off < bestOff || (off == bestOff && Squares(match, tolerance) < Squares(best, tolerance)))
                    best = match;
            }
        }
        return new Result(onsets.Count, taps.Count, best, tolerance);
    }

    // How far each tap is from its note, in whole milliseconds, early ones below zero, with the
    // notes matched by least squares to the taps at `matched`.
    private static List<int> Deviations(IReadOnlyList<double> onsets, IReadOnlyList<double> taps, IReadOnlyList<int> matched)
    {
        var meanOnset = matched.Average(i => onsets[i]);
        var meanTap = matched.Average(i => taps[i]);
        var spread = matched.Sum(i => (onsets[i] - meanOnset) * (onsets[i] - meanOnset));
        var scale = spread > 0
            ? matched.Sum(i => (onsets[i] - meanOnset) * (taps[i] - meanTap)) / spread
            : 1.0;
        scale = Math.Clamp(scale, 1 / (1 + MostTempoChange), 1 / (1 - MostTempoChange));
        var start = meanTap - scale * meanOnset;

        return onsets
            .Select((onset, i) => (int)Math.Round(taps[i] - (start + scale * onset), MidpointRounding.AwayFromZero))
            .ToList();
    }

    private static int Off(IReadOnlyList<int> deviations, int tolerance) => deviations.Count(off => Math.Abs(off) > tolerance);

    // How well the taps on time fit their notes: the fewer squared milliseconds, the better.
    private static long Squares(IReadOnlyList<int> deviations, int tolerance) =>
        deviations.Where(off => Math.Abs(off) <= tolerance).Sum(off => (long)off * off);

    private static bool IsNote(string value) => !DictationRhythm.IsRest(value);
}
