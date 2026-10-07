using System.Globalization;

namespace AcademiaAuditiva.Services;

/// <summary>
/// The rhythms of the dictations (MelodicDictation and RhythmDictation), counted in sixteenths,
/// the unit staff-editor.js counts a measure in. A note value is <c>w</c>, <c>h.</c>, <c>h</c>,
/// <c>q.</c>, <c>q</c>, <c>8.</c>, <c>8</c> or <c>16</c>, and its rest ends in <c>r</c>
/// (<c>qr</c>, <c>q.r</c>).
///
/// The first levels (1, 3 and 4) fill their bars at random (<see cref="RandomBars"/>). The
/// levels after them teach a figure (<see cref="Levels"/>): their bars are built of cells,
/// short figures that start on a beat, so the figure is heard where it belongs, and every
/// round has one (<see cref="Bars"/>). A melody in 6/8 is built of the cells of compound meter.
/// Both dictations are played at a tempo the student picks (<see cref="Tempo"/>), after a
/// count-in (<see cref="CountIn"/>).
/// </summary>
public static class DictationRhythm
{
    /// <summary>
    /// A figure the bars of a level are built of: its note values and rests, whether it is one
    /// the level teaches, and how often it is picked against the other cells that fit.
    /// </summary>
    public sealed record Cell(IReadOnlyList<string> Values, bool Teaches, int Weight)
    {
        /// <summary>How long the cell lasts, in sixteenths.</summary>
        public int Length => Values.Sum(Sixteenths);

        /// <summary>Whether the cell is silent: rests only.</summary>
        public bool IsSilent => Values.All(IsRest);

        /// <summary>Whether the cell starts with a rest.</summary>
        public bool StartsWithRest => IsRest(Values[0]);
    }

    /// <summary>
    /// A level that teaches a figure: its <c>rdLevel</c> value, the time signatures of its
    /// rounds, the note values and the rests the editor offers, and the cells of its bars.
    /// </summary>
    public sealed record Level(
        int Value,
        IReadOnlyList<string> TimeSignatures,
        IReadOnlyList<string> Durations,
        IReadOnlyList<string> Rests,
        IReadOnlyList<Cell> Cells);

    private static readonly Dictionary<string, int> ValueSixteenths = new()
    {
        ["w"] = 16, ["h."] = 12, ["h"] = 8, ["q."] = 6, ["q"] = 4, ["8."] = 3, ["8"] = 2, ["16"] = 1,
    };

    // How many rounds are drawn before one is made to start with a figure the level teaches.
    private const int Attempts = 100;

    /// <summary>The tempos of a dictation, in quarter notes a minute; the first is the default.</summary>
    public static IReadOnlyList<int> Tempos { get; } = [120, 90, 60];

    /// <summary>
    /// The levels that teach a figure, the <c>rdLevel</c> options after the first three. Their
    /// plain cells are the values of the levels before; the cells they teach are marked.
    /// </summary>
    public static IReadOnlyList<Level> Levels { get; } =
    [
        // Dotted notes: a dotted quarter and its eighth, a dotted half.
        new(5, ["4/4", "3/4", "2/4"], ["h.", "h", "q.", "q", "8"], ["qr"],
        [
            Plain(3, "q"), Plain(2, "8", "8"), Plain(1, "qr"), Plain(2, "h"),
            Taught(3, "q.", "8"), Taught(2, "h."),
        ]),
        // Sixteenths: four on a beat, two after an eighth or before it, one after a dotted eighth.
        new(6, ["4/4", "3/4", "2/4"], ["h", "q", "8.", "8", "16"], ["qr"],
        [
            Plain(2, "q"), Plain(2, "8", "8"), Plain(1, "qr"), Plain(1, "h"),
            Taught(2, "16", "16", "16", "16"), Taught(2, "8", "16", "16"), Taught(2, "16", "16", "8"),
            Taught(2, "8.", "16"),
        ]),
        // Syncopation: an eighth off the beat, a quarter between two eighths, a half note between two quarters.
        new(7, ["4/4", "3/4", "2/4"], ["h", "q", "8"], ["qr", "8r"],
        [
            Plain(2, "q"), Plain(2, "8", "8"), Plain(1, "qr"), Plain(1, "h"),
            Taught(2, "8r", "8"), Taught(3, "8", "q", "8"), Taught(2, "q", "h", "q"),
        ]),
        // Compound meter: 6/8, whose beat is a dotted quarter split in three eighths, or in a
        // quarter and an eighth either way round.
        new(8, ["6/8"], ["h.", "q.", "q", "8"], ["q.r"],
        [
            Plain(3, "q."), Taught(3, "q", "8"), Taught(2, "8", "q"), Taught(3, "8", "8", "8"),
            Plain(1, "q.r"), Plain(1, "h."),
        ]),
    ];

    /// <summary>The level of compound meter, whose cells also build the melodies in 6/8.</summary>
    public static Level Compound => Levels.Single(level => level.Value == 8);

    /// <summary>The level of an <c>rdLevel</c> value, or null for the first levels and any other value.</summary>
    public static Level? Find(int value) => Levels.FirstOrDefault(level => level.Value == value);

    /// <summary>The tempo of an <c>rdTempo</c> or <c>mdTempo</c> value: one of <see cref="Tempos"/>, the first by default.</summary>
    public static int Tempo(string? filter) =>
        int.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var tempo) && Tempos.Contains(tempo)
            ? tempo
            : Tempos[0];

    /// <summary>How long a note value or its rest lasts, in sixteenths: 6 for <c>q.</c> and <c>q.r</c>.</summary>
    public static int Sixteenths(string value) =>
        ValueSixteenths.TryGetValue(IsRest(value) ? value[..^1] : value, out var sixteenths)
            ? sixteenths
            : throw new ArgumentException($"Unknown note value '{value}'.", nameof(value));

    /// <summary>How long a note value or its rest lasts, in quarter notes: 1.5 for <c>q.</c>.</summary>
    public static double Beats(string value) => Sixteenths(value) / 4.0;

    /// <summary>Whether <paramref name="value"/> is a rest (<c>qr</c>, <c>q.r</c>).</summary>
    public static bool IsRest(string value) => value.EndsWith('r');

    /// <summary>The sixteenths in a bar of <paramref name="timeSignature"/>: 16 in 4/4, 12 in 3/4 and in 6/8.</summary>
    public static int BarLength(string timeSignature)
    {
        var (count, unit) = Meter(timeSignature);
        return count * 16 / unit;
    }

    /// <summary>The sixteenths in a beat of <paramref name="timeSignature"/>: 4 in 4/4, a dotted quarter (6) in 6/8.</summary>
    public static int BeatLength(string timeSignature) =>
        IsCompound(timeSignature) ? 6 : 16 / Meter(timeSignature).Unit;

    /// <summary>Whether <paramref name="timeSignature"/> is compound (6/8): its beats are dotted, three eighths each.</summary>
    public static bool IsCompound(string timeSignature)
    {
        var (count, unit) = Meter(timeSignature);
        return unit == 8 && count % 3 == 0;
    }

    /// <summary>
    /// The clicks that count the student in before a round in <paramref name="timeSignature"/>:
    /// a bar of beats, or two bars of 2/4, so there are four, and in 6/8 its six eighths, so
    /// its beats are heard in threes. Each click lasts <c>Beats</c> quarter notes, and is
    /// accented on the first beat of a bar, and in 6/8 on the first eighth of each beat.
    /// </summary>
    public static IReadOnlyList<(double Beats, bool Accent)> CountIn(string timeSignature)
    {
        var (count, unit) = Meter(timeSignature);
        var compound = IsCompound(timeSignature);
        var clicks = !compound && count == 2 ? 2 * count : count;
        return [.. Enumerable.Range(0, clicks).Select(i => (4.0 / unit, i % (compound ? 3 : count) == 0))];
    }

    /// <summary>
    /// Whether <paramref name="cell"/> can start <paramref name="offset"/> sixteenths into a bar
    /// of <paramref name="timeSignature"/>: on a beat, and ending in the bar. In 4/4, a cell
    /// that crosses the middle of the bar has to start it (a dotted half, or a half note between
    /// two quarters), so beat 3 can be seen: a half note is on beat 1 or 3.
    /// </summary>
    public static bool Fits(Cell cell, string timeSignature, int offset)
    {
        var bar = BarLength(timeSignature);
        var end = offset + cell.Length;
        if (offset % BeatLength(timeSignature) != 0 || end > bar) return false;
        return timeSignature != "4/4" || offset == 0 || offset >= bar / 2 || end <= bar / 2;
    }

    /// <summary>
    /// The note values of a round of <paramref name="measures"/> bars of
    /// <paramref name="timeSignature"/>, bar by bar, built of the cells of <paramref name="level"/>:
    /// each is picked by its weight among those that fit where it starts (<see cref="Fits"/>).
    /// Two silent cells never follow each other, and a <paramref name="melodic"/> round starts
    /// with a note, whose pitch is given. Every round has a cell the level teaches: when a
    /// hundred rounds drawn have none, the round starts with one.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Bars(
        Level level, string timeSignature, int measures, Random random, bool melodic)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var (bars, taught) = Draw(level, timeSignature, measures, random, melodic, first: null);
            if (taught) return bars;
        }

        var openings = level.Cells
            .Where(cell => cell.Teaches && Fits(cell, timeSignature, 0) && !(melodic && cell.StartsWithRest))
            .ToList();
        return Draw(level, timeSignature, measures, random, melodic, Pick(openings, random)).Bars;
    }

    /// <summary>
    /// The note values of a round of the first levels, bar by bar: each is picked at random
    /// among the <paramref name="durations"/> that still fit in the bar, and is a rest once in a
    /// while (<paramref name="restChance"/>), but not the first of a <paramref name="melodic"/> round.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> RandomBars(
        IReadOnlyList<string> durations, double restChance, string timeSignature, int measures, Random random, bool melodic)
    {
        var bars = new List<IReadOnlyList<string>>();
        for (var measure = 0; measure < measures; measure++)
        {
            var values = new List<string>();
            for (var left = BarLength(timeSignature); left > 0;)
            {
                var fitting = durations.Where(value => Sixteenths(value) <= left).ToList();
                if (fitting.Count == 0) break;
                var value = fitting[random.Next(fitting.Count)];
                var opening = melodic && measure == 0 && values.Count == 0;
                values.Add(!opening && random.NextDouble() < restChance ? value + "r" : value);
                left -= Sixteenths(value);
            }
            bars.Add(values);
        }
        return bars;
    }

    private static (IReadOnlyList<IReadOnlyList<string>> Bars, bool Taught) Draw(
        Level level, string timeSignature, int measures, Random random, bool melodic, Cell? first)
    {
        var bars = new List<IReadOnlyList<string>>();
        var taught = false;
        Cell? previous = null;
        for (var measure = 0; measure < measures; measure++)
        {
            var values = new List<string>();
            for (var offset = 0; offset < BarLength(timeSignature);)
            {
                var start = offset;
                var after = previous;
                var cell = after is null && first is not null
                    ? first
                    : Pick(
                        [.. level.Cells.Where(c => Fits(c, timeSignature, start)
                            && !(c.IsSilent && after?.IsSilent == true)
                            && !(melodic && after is null && c.StartsWithRest))],
                        random);
                values.AddRange(cell.Values);
                offset += cell.Length;
                taught |= cell.Teaches;
                previous = cell;
            }
            bars.Add(values);
        }
        return (bars, taught);
    }

    private static Cell Pick(IReadOnlyList<Cell> cells, Random random)
    {
        var ticket = random.Next(cells.Sum(cell => cell.Weight));
        foreach (var cell in cells)
        {
            ticket -= cell.Weight;
            if (ticket < 0) return cell;
        }
        throw new InvalidOperationException("No rhythmic cell fits.");
    }

    private static Cell Plain(int weight, params string[] values) => new(values, Teaches: false, weight);

    private static Cell Taught(int weight, params string[] values) => new(values, Teaches: true, weight);

    private static (int Count, int Unit) Meter(string timeSignature)
    {
        var parts = timeSignature.Split('/');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var unit)
            && count > 0
            && unit is 2 or 4 or 8 or 16
            ? (count, unit)
            : throw new ArgumentException($"Unknown time signature '{timeSignature}'.", nameof(timeSignature));
    }
}
