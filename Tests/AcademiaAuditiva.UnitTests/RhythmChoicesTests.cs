using AcademiaAuditiva.Services;
using Cell = AcademiaAuditiva.Services.DictationRhythm.Cell;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The rhythms GuessRhythmPattern offers: four rhythms of two bars at a level of RhythmDictation,
/// whose notes start in different places, so they can be told apart by ear, but each close to
/// the first one drawn; and any of them can be the one played.
/// </summary>
public class RhythmChoicesTests
{
    public static TheoryData<int> EveryLevel
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var level in DictationRhythm.RandomLevels.Select(level => level.Value)
                .Concat(DictationRhythm.Levels.Select(level => level.Value)))
            {
                data.Add(level);
            }
            return data;
        }
    }

    public static TheoryData<int> TheLevelsThatTeachAFigure
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var level in DictationRhythm.Levels)
            {
                data.Add(level.Value);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryLevel))]
    public void EveryRound_OffersFourRhythmsOfItsLevel_ThatAreHeardApart(int level)
    {
        var (timeSignatures, values) = Of(level);
        var drawn = new HashSet<string>();
        var random = new Random(level);

        for (var i = 0; i < 300; i++)
        {
            var round = RhythmChoices.Draw(level, random);
            var because = Describe(round);
            drawn.Add(round.TimeSignature);

            round.Options.Should().HaveCount(RhythmChoices.Count);
            round.Options.Select(option => option.Text).Should().OnlyHaveUniqueItems(because);
            round.Options.Select(option => string.Join(',', RhythmChoices.Onsets(option))).Should().OnlyHaveUniqueItems(
                "no two rhythms offered start their notes in the same places: {0}", because);
            round.Answer.Should().BeInRange(0, RhythmChoices.Count - 1);
            round.Played.Should().BeSameAs(round.Options[round.Answer]);
            foreach (var option in round.Options)
            {
                option.Bars.Should().HaveCount(RhythmChoices.Measures);
                foreach (var bar in option.Values)
                {
                    bar.Sum(DictationRhythm.Sixteenths).Should().Be(DictationRhythm.BarLength(round.TimeSignature),
                        "every bar is full: {0}", because);
                    bar.Should().Contain(value => !DictationRhythm.IsRest(value), "every bar has a note: {0}", because);
                    bar.Should().BeSubsetOf(values, "the rhythms are written in the values of level {0}: {1}", level, because);
                }
            }
        }

        drawn.Should().BeEquivalentTo(timeSignatures, "a round is in any time signature of level {0}", level);
    }

    [Theory]
    [MemberData(nameof(EveryLevel))]
    public void TheRhythms_AreTheFirstDrawn_WithAtMostTwoBeatsChanged(int level)
    {
        var random = new Random(level);

        for (var i = 0; i < 300; i++)
        {
            var round = RhythmChoices.Draw(level, random);
            var beat = DictationRhythm.BeatLength(round.TimeSignature);
            var onsets = round.Options.Select(RhythmChoices.Onsets).ToList();

            // The others differ from the first rhythm drawn in one beat or two, so it takes listening.
            onsets.Any(first => onsets.All(other => other == first || Close(first, other, beat))).Should().BeTrue(
                "one of the rhythms is the first drawn, and the others are it with a beat or two changed: {0}",
                Describe(round));
        }

        static bool Close(IReadOnlyList<int> first, IReadOnlyList<int> other, int beat) =>
            RhythmChoices.BeatsChanged(first, other, beat) is > 0 and <= RhythmChoices.MostBeatsChanged;
    }

    [Theory]
    [MemberData(nameof(TheLevelsThatTeachAFigure))]
    public void AtTheLevelsThatTeachAFigure_EveryRhythmIsBuiltOfItsFigures_AndHasOneItTeaches(int value)
    {
        var level = DictationRhythm.Find(value)!;
        var random = new Random(value);

        for (var i = 0; i < 300; i++)
        {
            var round = RhythmChoices.Draw(value, random);
            var because = Describe(round);

            foreach (var option in round.Options)
            {
                foreach (var bar in option.Bars)
                {
                    var offset = 0;
                    foreach (var cell in bar)
                    {
                        level.Cells.Should().Contain(cell, "the rhythms are built of the figures of level {0}: {1}", value, because);
                        DictationRhythm.Fits(cell, round.TimeSignature, offset).Should().BeTrue(
                            "a figure starts on a beat, where it can be seen: {0}", because);
                        offset += cell.Length;
                    }
                }
                option.Bars.SelectMany(bar => bar).Should().Contain(cell => cell.Teaches,
                    "every rhythm offered has a figure level {0} teaches: {1}", value, because);
                var cells = option.Bars.SelectMany(bar => bar).ToList();
                cells.Zip(cells.Skip(1)).Should().NotContain(pair => pair.First.IsSilent && pair.Second.IsSilent,
                    "two silent figures never follow each other, even across a barline: {0}", because);
            }
        }
    }

    [Fact]
    public void AtTheFirstLevel_TheFourRhythmsOfWholeAndHalfNotes_AreAlwaysOffered_AndEachIsPlayed()
    {
        string[] rhythms = ["w|bar|w", "w|bar|h|h", "h|h|bar|w", "h|h|bar|h|h"];
        var random = new Random(1);
        var played = new Dictionary<string, int>();
        var answers = new int[RhythmChoices.Count];

        for (var i = 0; i < 400; i++)
        {
            var round = RhythmChoices.Draw(1, random);

            round.TimeSignature.Should().Be("4/4");
            round.Options.Select(option => option.Text).Should().BeEquivalentTo(rhythms);
            played[round.Played.Text] = played.GetValueOrDefault(round.Played.Text) + 1;
            answers[round.Answer]++;
        }

        played.Keys.Should().BeEquivalentTo(rhythms);
        played.Values.Should().OnlyContain(count => count >= 70, "any rhythm is the one played, as often as the others");
        answers.Should().OnlyContain(count => count >= 70, "the one played is anywhere among the four");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void Draw_AtALevelRhythmDictationHasnt_IsAnError(int level)
    {
        var act = () => RhythmChoices.Draw(level, new Random(0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("w|w", "0,16")]
    [InlineData("q q h|w", "0,4,8,16")]
    [InlineData("q 8r 8 h|wr", "0,6,8")]
    [InlineData("qr q h|h. q", "4,8,16,28")]
    [InlineData("q. 8 q|8 8 8 8 qr", "0,6,8,12,14,16,18")]
    public void Onsets_AreWhereTheNotesStart_InSixteenths(string rhythm, string onsets)
    {
        RhythmChoices.Onsets(Rhythm(rhythm)).Should().Equal(onsets.Split(',').Select(int.Parse));
    }

    [Theory]
    [InlineData("0,4,8,12", "0,4,8,12", 4, 0)]
    [InlineData("0,4,8,12", "0,6,8,12", 4, 1)]
    [InlineData("0,4,8,12", "0,8", 4, 2)]
    [InlineData("0,8,16,24", "0,4,8,12,16,20,24,28", 4, 4)]
    [InlineData("0,6", "0,2,4,6", 6, 1)]
    [InlineData("0,6", "0,4,6,8", 6, 2)]
    public void BeatsChanged_CountsTheBeatsWhereOnlyOneOfTheRhythmsStartsANote(string onsets, string others, int beat, int changed)
    {
        RhythmChoices.BeatsChanged(Ints(onsets), Ints(others), beat).Should().Be(changed);
        RhythmChoices.BeatsChanged(Ints(others), Ints(onsets), beat).Should().Be(changed);
    }

    [Fact]
    public void ARhythm_IsWrittenAsARhythmDictationAnswer()
    {
        var rhythm = Rhythm("q 8r 8 h|w");

        rhythm.Text.Should().Be("q|8r|8|h|bar|w");
        rhythm.Values.Should().BeEquivalentTo(new[] { new[] { "q", "8r", "8", "h" }, new[] { "w" } },
            options => options.WithStrictOrdering());
    }

    // The time signatures of a level, and the note values and rests it writes rhythms in.
    private static (IReadOnlyList<string> TimeSignatures, HashSet<string> Values) Of(int value)
    {
        if (DictationRhythm.Find(value) is { } taught)
            return (taught.TimeSignatures, taught.Durations.Concat(taught.Rests).ToHashSet());
        var plain = DictationRhythm.FindRandom(value)!;
        return (plain.TimeSignatures, plain.Durations.Concat(plain.Rests).ToHashSet());
    }

    // A rhythm of bars separated by "|", each of values separated by spaces, one cell each.
    private static RhythmChoices.Rhythm Rhythm(string bars) =>
        new([.. bars.Split('|').Select(bar =>
            bar.Split(' ').Select(value => new Cell([value], Teaches: false, Weight: 1)).ToList())]);

    private static List<int> Ints(string values) => [.. values.Split(',').Select(int.Parse)];

    private static string Describe(RhythmChoices.Round round) =>
        $"{round.TimeSignature}: {string.Join(" / ", round.Options.Select(option => option.Text))}";
}
