using System.Globalization;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The rhythms of the dictations: the levels that teach a figure build their bars of figures
/// that start on a beat, every round has the one it teaches, and the student is counted in.
/// </summary>
public class DictationRhythmTests
{
    public static TheoryData<int, string> LevelsInEachOfTheirTimeSignatures
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var level in DictationRhythm.Levels)
            {
                foreach (var timeSignature in level.TimeSignatures)
                {
                    data.Add(level.Value, timeSignature);
                }
            }
            return data;
        }
    }

    [Theory]
    [InlineData("w", 16)]
    [InlineData("h.", 12)]
    [InlineData("h", 8)]
    [InlineData("q.", 6)]
    [InlineData("q", 4)]
    [InlineData("8.", 3)]
    [InlineData("8", 2)]
    [InlineData("16", 1)]
    [InlineData("qr", 4)]
    [InlineData("q.r", 6)]
    [InlineData("8r", 2)]
    public void Sixteenths_CountsANoteValueOrItsRest(string value, int sixteenths)
    {
        DictationRhythm.Sixteenths(value).Should().Be(sixteenths);
        DictationRhythm.Beats(value).Should().Be(sixteenths / 4.0);
        DictationRhythm.IsRest(value).Should().Be(value.EndsWith('r'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("r")]
    [InlineData("q..")]
    [InlineData("32")]
    public void Sixteenths_OfAnUnknownValue_IsAnError(string value)
    {
        var act = () => DictationRhythm.Sixteenths(value);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("4/4", 16, 4, false)]
    [InlineData("3/4", 12, 4, false)]
    [InlineData("2/4", 8, 4, false)]
    [InlineData("6/8", 12, 6, true)]
    public void ATimeSignature_HasItsBarAndItsBeat(string timeSignature, int bar, int beat, bool compound)
    {
        DictationRhythm.BarLength(timeSignature).Should().Be(bar);
        DictationRhythm.BeatLength(timeSignature).Should().Be(beat);
        DictationRhythm.IsCompound(timeSignature).Should().Be(compound);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("4/")]
    [InlineData("4/5")]
    [InlineData("0/4")]
    [InlineData("x/4")]
    [InlineData("4/4/4")]
    public void AnUnknownTimeSignature_IsAnError(string timeSignature)
    {
        var act = () => DictationRhythm.BarLength(timeSignature);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("4/4", "x...", 1.0)]
    [InlineData("3/4", "x..", 1.0)]
    [InlineData("2/4", "x.x.", 1.0)]
    [InlineData("6/8", "x..x..", 0.5)]
    public void CountIn_ClicksTheBeats_AccentingTheFirstOfEachBar(string timeSignature, string accents, double beats)
    {
        // Two bars of 2/4, so there are four clicks, and 6/8 in eighths, so its beats are heard in threes.
        DictationRhythm.CountIn(timeSignature).Should().Equal(accents.Select(accent => (beats, accent == 'x')));
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("90", 90)]
    [InlineData("60", 60)]
    [InlineData(null, 120)]
    [InlineData("", 120)]
    [InlineData("100", 120)]
    [InlineData(" 90", 120)]
    [InlineData("+60", 120)]
    [InlineData("60.0", 120)]
    [InlineData("fast", 120)]
    public void Tempo_IsOneOfTheTempos_TheFirstByDefault(string? filter, int tempo)
    {
        DictationRhythm.Tempo(filter).Should().Be(tempo);
    }

    [Fact]
    public void TheFilters_OfferEveryLevelAndEveryTempo()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dictation-rhythm-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var tempos = DictationRhythm.Tempos.Select(Invariant).ToList();

        Options(db, "RhythmDictation", "rdLevel").Should().Equal(
            new[] { "1", "3", "4" }.Concat(DictationRhythm.Levels.Select(level => Invariant(level.Value))));
        Options(db, "RhythmDictation", "rdTempo").Should().Equal(tempos);
        Options(db, "MelodicDictation", "mdTempo").Should().Equal(tempos);
    }

    [Fact]
    public void EveryLevel_OffersTheValuesOfItsFigures_LongestFirst()
    {
        foreach (var level in DictationRhythm.Levels)
        {
            level.Cells.SelectMany(cell => cell.Values).Distinct().Should().BeEquivalentTo(
                level.Durations.Concat(level.Rests), "level {0} offers the values of its figures, and only them", level.Value);
            level.Durations.Should().NotContain(value => DictationRhythm.IsRest(value));
            level.Rests.Should().OnlyContain(value => DictationRhythm.IsRest(value));
            level.Durations.Select(DictationRhythm.Sixteenths).Should().BeInDescendingOrder()
                .And.OnlyHaveUniqueItems("the editor's buttons go from the longest note value to the shortest");
            level.Cells.Should().Contain(cell => cell.Teaches, "level {0} teaches a figure", level.Value)
                .And.OnlyContain(cell => cell.Weight > 0);
        }
    }

    [Theory]
    [MemberData(nameof(LevelsInEachOfTheirTimeSignatures))]
    public void EveryBeat_HasAFigureThatStartsWithANote(int value, string timeSignature)
    {
        // So a bar can always be filled, even after a silent figure, and a melody starts on a note.
        var level = DictationRhythm.Find(value)!;
        var beat = DictationRhythm.BeatLength(timeSignature);

        level.Cells.Should().OnlyContain(cell => cell.Length % beat == 0, "a figure lasts whole beats, so the next one starts on a beat");
        for (var offset = 0; offset < DictationRhythm.BarLength(timeSignature); offset += beat)
        {
            var start = offset;
            level.Cells.Should().Contain(cell => !cell.StartsWithRest && DictationRhythm.Fits(cell, timeSignature, start),
                "a figure of level {0} starts with a note on beat {1} of {2}", value, start / beat + 1, timeSignature);
        }
        level.Cells.Should().Contain(cell => cell.Teaches && !cell.StartsWithRest && DictationRhythm.Fits(cell, timeSignature, 0),
            "a round of level {0} in {1} can start with a figure it teaches", value, timeSignature);
    }

    [Theory]
    [InlineData("4/4", "q", 0, true)]
    [InlineData("4/4", "q", 12, true)]
    [InlineData("4/4", "q", 2, false)]
    [InlineData("4/4", "h", 0, true)]
    [InlineData("4/4", "h", 4, false)]
    [InlineData("4/4", "h", 8, true)]
    [InlineData("4/4", "h", 12, false)]
    [InlineData("4/4", "q.,8", 4, false)]
    [InlineData("4/4", "h.", 0, true)]
    [InlineData("4/4", "h.", 4, false)]
    [InlineData("4/4", "q,h,q", 0, true)]
    [InlineData("3/4", "h", 4, true)]
    [InlineData("3/4", "q,h,q", 0, false)]
    [InlineData("2/4", "h.", 0, false)]
    [InlineData("6/8", "q,8", 6, true)]
    [InlineData("6/8", "q,8", 3, false)]
    [InlineData("6/8", "h.", 0, true)]
    [InlineData("6/8", "h.", 6, false)]
    public void AFigure_StartsOnABeat_AndEndsInTheBar_AndCrossesTheMiddleOf44FromItsStart(
        string timeSignature, string values, int offset, bool fits)
    {
        var cell = new DictationRhythm.Cell(values.Split(','), Teaches: false, Weight: 1);

        DictationRhythm.Fits(cell, timeSignature, offset).Should().Be(fits);
    }

    [Theory]
    [MemberData(nameof(LevelsInEachOfTheirTimeSignatures))]
    public void EveryRound_IsBuiltOfTheFiguresOfItsLevel_AndHasOneItTeaches(int value, string timeSignature)
    {
        var level = DictationRhythm.Find(value)!;
        var plain = level.Cells.Where(cell => !cell.Teaches).ToList();
        var silent = level.Cells.Where(cell => cell.IsSilent).SelectMany(cell => cell.Values).ToHashSet();
        var random = new Random(value);

        foreach (var measures in new[] { 2, 4 })
        {
            foreach (var melodic in new[] { false, true })
            {
                for (var round = 0; round < 100; round++)
                {
                    var bars = DictationRhythm.Bars(level, timeSignature, measures, random, melodic);
                    var values = bars.SelectMany(bar => bar).ToList();
                    var because = string.Join(" | ", bars.Select(bar => string.Join(" ", bar)));

                    bars.Should().HaveCount(measures);
                    foreach (var bar in bars)
                    {
                        CanBuild(bar, level.Cells, timeSignature).Should().BeTrue(
                            "the bars are built of figures of level {0} that start on a beat: {1}", value, because);
                    }
                    bars.Should().Contain(bar => !CanBuild(bar, plain, timeSignature),
                        "every round has a figure level {0} teaches: {1}", value, because);
                    for (var i = 1; i < values.Count; i++)
                    {
                        (silent.Contains(values[i - 1]) && silent.Contains(values[i])).Should().BeFalse(
                            "two silent figures never follow each other: {0}", because);
                    }
                    if (melodic)
                    {
                        DictationRhythm.IsRest(values[0]).Should().BeFalse("a melody starts on its given note: {0}", because);
                    }
                }
            }
        }
    }

    [Fact]
    public void ARound_WithoutTheFigureItTeaches_StartsWithIt()
    {
        // A figure so rare that a hundred rounds drawn hardly ever have it.
        var level = new DictationRhythm.Level(9, ["4/4"], ["h", "q"], [],
            [new(["q"], Teaches: false, Weight: 10_000), new(["h"], Teaches: true, Weight: 1)]);

        for (var seed = 0; seed < 20; seed++)
        {
            var bars = DictationRhythm.Bars(level, "4/4", 2, new Random(seed), melodic: false);

            bars.SelectMany(bar => bar).Should().Contain("h");
        }
    }

    // Whether the bar can be split into some of the cells, each fitting where it starts.
    private static bool CanBuild(IReadOnlyList<string> bar, IReadOnlyList<DictationRhythm.Cell> cells, string timeSignature)
    {
        return From(0, 0);

        bool From(int index, int offset) =>
            index == bar.Count
                ? offset == DictationRhythm.BarLength(timeSignature)
                : cells.Any(cell => index + cell.Values.Count <= bar.Count
                    && cell.Values.SequenceEqual(bar.Skip(index).Take(cell.Values.Count))
                    && DictationRhythm.Fits(cell, timeSignature, offset)
                    && From(index + cell.Values.Count, offset + cell.Length));
    }

    private static List<string> Options(ApplicationDbContext db, string exercise, string filter) =>
        [.. ExerciseFilterPresets.Groups(db.Exercises.Single(e => e.Name == exercise).FiltersJson)
            .Single(group => group.Name == filter)
            .Options.Select(option => option.Value)];

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
