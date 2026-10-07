using Cell = AcademiaAuditiva.Services.DictationRhythm.Cell;

namespace AcademiaAuditiva.Services;

/// <summary>
/// The rhythms GuessRhythmPattern offers: <see cref="Count"/> rhythms of <see cref="Measures"/>
/// bars at a level of RhythmDictation (<see cref="DictationRhythm"/>), one of which is played.
/// The first is drawn as a dictation is; each of the others is it with one or two places drawn
/// again: a figure or two of a bar. Their notes start in different places, so the one played
/// can be told by ear, but differ from the first one in at most <see cref="MostBeatsChanged"/>
/// beats, so it takes listening. Every bar has a note, and at the levels that teach a figure,
/// every rhythm has one. They are shown in a random order, and any of them is the one played.
/// </summary>
public static class RhythmChoices
{
    /// <summary>How many rhythms a round offers.</summary>
    public const int Count = 4;

    /// <summary>How many bars a rhythm lasts.</summary>
    public const int Measures = 2;

    /// <summary>In how many beats, at most, the notes of a rhythm start elsewhere than in the first one drawn.</summary>
    public const int MostBeatsChanged = 2;

    // Rhythms changed from the first one before giving up on it, and first rhythms drawn.
    private const int Attempts = 1000;
    private const int FirstRhythms = 10;

    /// <summary>A rhythm offered: its bars, each the cells it is built of.</summary>
    public sealed record Rhythm(IReadOnlyList<IReadOnlyList<Cell>> Bars)
    {
        /// <summary>The note values of each bar.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Values => [.. Bars.Select(DictationRhythm.Values)];

        /// <summary>The rhythm written as a RhythmDictation answer: <c>q|q|h|bar|w</c>.</summary>
        public string Text => string.Join("|bar|", Values.Select(bar => string.Join("|", bar)));
    }

    /// <summary>A round: its time signature, the rhythms in the order shown, and which of them is played.</summary>
    public sealed record Round(string TimeSignature, IReadOnlyList<Rhythm> Options, int Answer)
    {
        /// <summary>The rhythm played.</summary>
        public Rhythm Played => Options[Answer];
    }

    /// <summary>A round at the <c>rdLevel</c> value <paramref name="level"/> (<see cref="DictationRhythm.IsLevel"/>).</summary>
    public static Round Draw(int level, Random random)
    {
        var taught = DictationRhythm.Find(level);
        var plain = DictationRhythm.FindRandom(level);
        if (taught is null && plain is null)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Not a level of RhythmDictation.");
        var timeSignatures = taught?.TimeSignatures ?? plain!.TimeSignatures;
        var timeSignature = timeSignatures[random.Next(timeSignatures.Count)];
        var source = new Source(timeSignature, taught, plain);

        for (var draw = 0; draw < FirstRhythms; draw++)
        {
            var first = source.Draw(random);
            if (!Audible(first)) continue;
            var options = new List<Rhythm> { first };
            var heard = new HashSet<string> { Key(Onsets(first)) };
            for (var attempt = 0; attempt < Attempts && options.Count < Count; attempt++)
            {
                var changed = Change(first, source, random);
                var onsets = Onsets(changed);
                if (!Audible(changed)) continue;
                if (taught is not null && !changed.Bars.Any(bar => bar.Any(cell => cell.Teaches))) continue;
                if (BeatsChanged(Onsets(first), onsets, DictationRhythm.BeatLength(timeSignature)) > MostBeatsChanged) continue;
                if (heard.Add(Key(onsets))) options.Add(changed);
            }
            if (options.Count < Count) continue;

            for (var i = options.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }
            return new Round(timeSignature, options, random.Next(Count));
        }

        throw new InvalidOperationException($"No {Count} rhythms of level {level} in {timeSignature} can be told apart.");
    }

    /// <summary>
    /// Where the notes of <paramref name="rhythm"/> start, in sixteenths from its first beat: what
    /// is heard first of it. Rhythms that only differ in how long their notes last, a note against
    /// a shorter one and a rest, are too close to tell apart, so they are never offered together.
    /// </summary>
    public static IReadOnlyList<int> Onsets(Rhythm rhythm)
    {
        var onsets = new List<int>();
        var time = 0;
        foreach (var value in rhythm.Values.SelectMany(bar => bar))
        {
            if (!DictationRhythm.IsRest(value)) onsets.Add(time);
            time += DictationRhythm.Sixteenths(value);
        }
        return onsets;
    }

    /// <summary>In how many beats of <paramref name="beat"/> sixteenths two rhythms start a note where the other doesn't.</summary>
    public static int BeatsChanged(IReadOnlyList<int> onsets, IReadOnlyList<int> others, int beat) =>
        onsets.Except(others).Concat(others.Except(onsets)).Select(onset => onset / beat).Distinct().Count();

    // The first rhythm with one or two places drawn again: one or two figures in a row of a bar.
    private static Rhythm Change(Rhythm first, Source source, Random random)
    {
        var bars = first.Bars.Select(bar => bar.ToList()).ToList();
        var changes = 1 + random.Next(2);
        for (var change = 0; change < changes; change++)
        {
            var b = random.Next(bars.Count);
            var bar = bars[b];
            var start = random.Next(bar.Count);
            var count = Math.Min(1 + random.Next(2), bar.Count - start);
            var from = bar.Take(start).Sum(cell => cell.Length);
            var to = from + bar.Skip(start).Take(count).Sum(cell => cell.Length);
            var before = start > 0 ? bar[start - 1] : b > 0 ? bars[b - 1][^1] : null;
            var after = start + count < bar.Count ? bar[start + count] : b + 1 < bars.Count ? bars[b + 1][0] : null;
            bar.RemoveRange(start, count);
            bar.InsertRange(start, source.Fill(from, to, before, after, random));
        }
        return new Rhythm(bars);
    }

    private static string Key(IReadOnlyList<int> onsets) => string.Join(',', onsets);

    // Every bar of a rhythm offered has a note: the first levels may draw a bar of rests.
    private static bool Audible(Rhythm rhythm) =>
        rhythm.Values.All(bar => bar.Any(value => !DictationRhythm.IsRest(value)));

    // How a level draws a rhythm and fills part of a bar again: of its cells, or at random with its
    // note values, each a cell of its own.
    private sealed record Source(string TimeSignature, DictationRhythm.Level? Taught, DictationRhythm.RandomLevel? Plain)
    {
        public Rhythm Draw(Random random) => Taught is { } level
            ? new Rhythm(DictationRhythm.Figures(level, TimeSignature, Measures, random, melodic: false))
            : new Rhythm([.. DictationRhythm.RandomBars(Plain!.Durations, Plain.RestChance, TimeSignature, Measures, random, melodic: false)
                .Select(Cells)]);

        public IReadOnlyList<Cell> Fill(int from, int to, Cell? before, Cell? after, Random random) => Taught is { } level
            ? DictationRhythm.Fill(level, TimeSignature, from, to, before, after, random)
            : Cells(DictationRhythm.RandomFill(Plain!.Durations, Plain.RestChance, to - from, random));

        private static IReadOnlyList<Cell> Cells(IReadOnlyList<string> values) =>
            [.. values.Select(value => new Cell([value], Teaches: false, Weight: 1))];
    }
}
