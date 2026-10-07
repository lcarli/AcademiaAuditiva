using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class RhythmTapsTests
{
    // A quarter, two eighths and a half, then a whole, at 120 quarter notes a minute.
    private static readonly double[] Onsets = [0, 500, 750, 1000, 2000];

    private const string Round = """
        {"tempo":120,"answerString":"q|8|8|h|bar|w","melody":[
          {"type":"note","note":"C5","durationBeats":1.0,"durationLabel":"q"},
          {"type":"note","note":"C5","durationBeats":0.5,"durationLabel":"8"},
          {"type":"note","note":"C5","durationBeats":0.5,"durationLabel":"8"},
          {"type":"note","note":"C5","durationBeats":2.0,"durationLabel":"h"},
          {"type":"note","note":"C5","durationBeats":4.0,"durationLabel":"w"}]}
        """;

    [Fact]
    public void ATapOnEachNote_PlaysTheRhythm()
    {
        var result = RhythmTaps.Check(Onsets, Onsets);

        result.Should().BeEquivalentTo(new { Notes = 5, Taps = 5, Tolerance = 100 });
        result.Deviations.Should().Equal(0, 0, 0, 0, 0);
        result.InTime().Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1234, 1.0)]
    [InlineData(300, 0.9)]
    [InlineData(300, 1.1)]
    [InlineData(0, 1 / 1.15)]
    [InlineData(0, 1 / 0.85)]
    public void TheTaps_MayStartAnyTime_AndBeUpToFifteenPercentFasterOrSlower(double start, double stretch)
    {
        var taps = Onsets.Select(onset => start + onset * stretch).ToList();

        var result = RhythmTaps.Check(Onsets, taps);

        result.Deviations.Should().OnlyContain(off => off == 0);
        result.InTime().Should().BeTrue();
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.3)]
    public void TheTaps_MuchFasterOrSlower_AreOffTime(double stretch)
    {
        var taps = Onsets.Select(onset => onset * stretch).ToList();

        var result = RhythmTaps.Check(Onsets, taps);

        result.Deviations.Should().HaveCount(5).And.Contain(off => Math.Abs(off) > result.Tolerance);
        result.InTime().Should().BeFalse();
    }

    [Fact]
    public void ATapFarFromItsNote_IsOffTime_AndTheOthersAreNot()
    {
        double[] taps = [0, 500, 900, 1000, 2000];

        var result = RhythmTaps.Check(Onsets, taps);

        result.Deviations.Should().Equal(0, 0, 150, 0, 0);
        result.InTime().Should().BeFalse();
    }

    // Matched by least squares, a late tap among four would put the half off too: -17, -57, 183, -109.
    [Fact]
    public void ALateTap_IsTheOnlyOneOff_ThoughItWouldPullTheMatchOfTheOthers()
    {
        // A quarter, two eighths and a half at 90 quarter notes a minute; the second eighth tapped late.
        double[] onsets = [0, 2000.0 / 3, 1000, 4000.0 / 3];
        double[] taps = [0, 667, 1266, 1333];

        var result = RhythmTaps.Check(onsets, taps);

        result.Deviations!.Select(off => Math.Abs(off) > result.Tolerance).Should().Equal(false, false, true, false);
        result.Deviations![2].Should().BeCloseTo(266, 2);
    }

    [Theory]
    [InlineData(1, 180)]
    [InlineData(1, -180)]
    [InlineData(2, 180)]
    [InlineData(2, -180)]
    [InlineData(3, 180)]
    [InlineData(3, -180)]
    public void AnyTapFarFromItsNote_IsTheOnlyOneOff(int late, int by)
    {
        var taps = Onsets.Select((onset, i) => i == late ? onset + by : onset).ToList();

        var result = RhythmTaps.Check(Onsets, taps);

        result.Deviations.Should().Equal(Onsets.Select((_, i) => i == late ? by : 0));
        result.InTime().Should().BeFalse();
    }

    [Fact]
    public void TheLastTapLate_IsHeardAsASlowerTempo()
    {
        double[] taps = [0, 500, 750, 1000, 2180];

        var result = RhythmTaps.Check(Onsets, taps);

        result.InTime().Should().BeTrue("the taps fit the rhythm a little slower than it was played");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    public void TapsThatAreNotOnePerNote_AreWrong_AndNotMatchedToTheNotes(int count)
    {
        var taps = Enumerable.Range(0, count).Select(i => i * 400.0).ToList();

        var result = RhythmTaps.Check(Onsets, taps);

        result.Should().BeEquivalentTo(new { Notes = 5, Taps = count });
        result.Deviations.Should().BeNull();
        result.InTime().Should().BeFalse();
    }

    [Theory]
    [InlineData(new double[] { 0, 1000, 2000 }, 100)]
    [InlineData(new double[] { 0, 500, 750, 1000 }, 100)]
    [InlineData(new double[] { 0, 125, 250, 375 }, 50)]
    [InlineData(new double[] { 0, 166.667, 333.333, 1000 }, 66)]
    public void ATap_MayBeOff_ByOneHundredMilliseconds_OrFortyPercentOfTheShortestNote(double[] onsets, int tolerance) =>
        RhythmTaps.Check(onsets, onsets).Tolerance.Should().Be(tolerance);

    [Theory]
    [InlineData(null, new double[0])]
    [InlineData("", new double[0])]
    [InlineData("  ", new double[0])]
    [InlineData("0", new double[] { 0 })]
    [InlineData("0,498,1003", new double[] { 0, 498, 1003 })]
    [InlineData("0, 500 ,500", new double[] { 0, 500, 500 })]
    public void Parse_ReadsTheTaps(string? guess, double[] taps) =>
        RhythmTaps.Parse(guess).Should().Equal(taps);

    [Theory]
    [InlineData("0,500,400")]
    [InlineData("0,-5")]
    [InlineData("+5")]
    [InlineData("0,1.5")]
    [InlineData("0,,500")]
    [InlineData("0;500")]
    [InlineData("q|8|8|h|bar|w")]
    [InlineData("99999999999")]
    public void Parse_RefusesWhatIsNoListOfTaps(string guess) =>
        RhythmTaps.Parse(guess).Should().BeNull();

    [Fact]
    public void Parse_RefusesMoreTapsThanARhythmCouldHave()
    {
        RhythmTaps.Parse(string.Join(",", Enumerable.Range(0, RhythmTaps.MostTaps))).Should().HaveCount(RhythmTaps.MostTaps);
        RhythmTaps.Parse(string.Join(",", Enumerable.Range(0, RhythmTaps.MostTaps + 1))).Should().BeNull();
        RhythmTaps.Parse(new string('0', 1000)).Should().BeNull();
    }

    [Fact]
    public void Onsets_AreWhenTheNotesStart_AtTheTempo_RestsAside()
    {
        var round = JObject.Parse("""
            {"tempo":90,"melody":[
              {"type":"note","durationBeats":1.0},
              {"type":"rest","durationBeats":0.5},
              {"type":"note","durationBeats":0.5},
              {"type":"note","durationBeats":2.0}]}
            """);

        RhythmTaps.Onsets(round).Should().Equal(new[] { 0, 1000, 4000.0 / 3 }, (onset, expected) => Math.Abs(onset - expected) < 1e-9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Draw_GivesTwoFullBarsOfTheLevel_WithANoteInEach_AndThreeAtLeast(int level)
    {
        var random = new Random(level);
        var timeSignatures = DictationRhythm.Find(level)?.TimeSignatures ?? DictationRhythm.FindRandom(level)!.TimeSignatures;

        for (var round = 0; round < 200; round++)
        {
            var (timeSignature, bars) = RhythmTaps.Draw(level, random);

            timeSignatures.Should().Contain(timeSignature);
            bars.Should().HaveCount(RhythmTaps.Measures);
            bars.Should().OnlyContain(bar => bar.Sum(value => DictationRhythm.Beats(value)) == DictationRhythm.BarLength(timeSignature) / 4.0);
            bars.Should().OnlyContain(bar => bar.Any(value => !DictationRhythm.IsRest(value)));
            bars.Sum(bar => bar.Count(value => !DictationRhythm.IsRest(value))).Should().BeGreaterThanOrEqualTo(RhythmTaps.FewestNotes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void Draw_RefusesALevelRhythmDictationHasNot(int level) =>
        FluentActions.Invoking(() => RhythmTaps.Draw(level, new Random(1))).Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData("4/4", 120, 7.75)]
    [InlineData("3/4", 60, 11.5)]
    [InlineData("2/4", 90, 7.667)]
    [InlineData("6/8", 120, 5.875)]
    public void TapsFrom_IsHalfAClickBeforeTheBarAfterTheSecondCountIn(string timeSignature, int tempo, double seconds) =>
        RhythmTaps.TapsFrom(timeSignature, tempo).Should().Be(seconds);

    [Fact]
    public void TheValidator_SaysHowTheTapsWent()
    {
        var validator = new RhythmTapValidator();

        var right = validator.Validate("0,498,752,1003,2001", Round);
        var wrong = validator.Validate("0,500", Round);
        var garbled = validator.Validate("q|8|8|h|bar|w", Round);

        right.IsCorrect.Should().BeTrue();
        right.CanonicalAnswer.Should().Be("q|8|8|h|bar|w");
        right.Detail.Should().BeOfType<RhythmTaps.Result>().Which.Deviations.Should().HaveCount(5);
        wrong.IsCorrect.Should().BeFalse();
        wrong.Detail.Should().BeEquivalentTo(new { Notes = 5, Taps = 2, Deviations = (IReadOnlyList<int>?)null });
        garbled.IsCorrect.Should().BeFalse();
        garbled.Detail.Should().BeEquivalentTo(new { Notes = 5, Taps = 0 });
        ((IExerciseValidator)validator).AnswerOf(Round).Should().Be("q|8|8|h|bar|w", "free practice shows the rhythm played");
    }
}
