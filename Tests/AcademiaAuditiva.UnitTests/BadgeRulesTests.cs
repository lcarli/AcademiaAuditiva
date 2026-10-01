using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every badge rule, with the smallest history that earns the badge and one
/// that just misses it. Answers are one minute apart unless a test moves the
/// clock; results are written as strings, "1" for right and "0" for wrong.
/// </summary>
public class BadgeRulesTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void NoAnswers_EarnNothing()
    {
        new History().Exercise(1).Evaluate().Should().BeEmpty();
    }

    [Fact]
    public void FirstAnswer_EarnsOnlyFirstSession_EvenWhenWrong()
    {
        new History().Exercise(1).Answer(1, "0").Evaluate().Should().Equal(BadgeKeys.FirstSession);
    }

    [Fact]
    public void BadgesAlreadyEarned_AreNotAwardedAgain()
    {
        new History().Exercise(1).Answer(1, "1").Evaluate(BadgeKeys.FirstSession).Should().BeEmpty();
    }

    [Fact]
    public void AnswersToUnknownExercises_StillCountForBadgesThatIgnoreTheTaxonomy()
    {
        // 21 answers a minute apart: a 20-minute run that starts with three misses.
        new History().Answer(42, "00011" + Ones(16)).Evaluate().Should().Equal(
            BadgeKeys.FirstSession, BadgeKeys.Marathon20Min, BadgeKeys.ComebackKid, BadgeKeys.ResilientEar);
    }

    [Theory]
    [InlineData(2, false, false)]
    [InlineData(3, true, false)]
    [InlineData(5, true, true)]
    public void DayStreaks_CountConsecutiveDays(int days, bool threeDays, bool fiveDays)
    {
        var history = new History().Exercise(1);
        for (var day = 0; day < days; day++) history.At(Start.AddDays(day)).Answer(1, "1");

        var awarded = history.Evaluate();

        awarded.Contains(BadgeKeys.ThreeDays).Should().Be(threeDays);
        awarded.Contains(BadgeKeys.FiveDays).Should().Be(fiveDays);
    }

    [Fact]
    public void DayStreaks_BreakOnAMissedDay()
    {
        var history = new History().Exercise(1);
        foreach (var day in new[] { 0, 1, 3, 4 }) history.At(Start.AddDays(day)).Answer(1, "1");

        history.Evaluate().Should().NotContain(BadgeKeys.ThreeDays);
    }

    [Fact]
    public void DayStreaks_FollowThePlayersCalendar()
    {
        // 01:30 UTC is still the evening before in Toronto (UTC-4 in September).
        var history = new History().Exercise(1)
            .At(new DateTime(2026, 9, 2, 1, 30, 0, DateTimeKind.Utc)).Answer(1, "1")
            .At(new DateTime(2026, 9, 2, 15, 0, 0, DateTimeKind.Utc)).Answer(1, "1")
            .At(new DateTime(2026, 9, 3, 15, 0, 0, DateTimeKind.Utc)).Answer(1, "1");

        history.EvaluateIn(Toronto).Should().Contain(BadgeKeys.ThreeDays);
        history.Evaluate().Should().NotContain(BadgeKeys.ThreeDays, "in UTC the answers fall on two days");
    }

    [Theory]
    [InlineData(21, true)]
    [InlineData(20, false)]
    public void Marathon_Needs20MinutesOfPractice(int answersOneMinuteApart, bool earned)
    {
        new History().Exercise(1).Answer(1, Ones(answersOneMinuteApart)).Evaluate()
            .Contains(BadgeKeys.Marathon20Min).Should().Be(earned);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Marathon_AllowsBreaksOfUpTo5Minutes(int breakMinutes, bool earned)
    {
        var history = new History().Exercise(1).Answer(1, "1");
        for (var i = 0; i < 4; i++) history.Pause(TimeSpan.FromMinutes(breakMinutes)).Answer(1, "1");

        // Four 5-minute breaks span exactly 20 minutes.
        history.Evaluate().Contains(BadgeKeys.Marathon20Min).Should().Be(earned);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void FaithfulPractitioner_Needs30Sessions(int sessions, bool earned)
    {
        var history = new History().Exercise(1);
        for (var s = 0; s < sessions; s++) history.At(Start.AddDays(s)).Answer(1, Ones(5));

        history.Evaluate().Contains(BadgeKeys.FaithfulPractitioner).Should().Be(earned);
    }

    [Fact]
    public void Sessions_NeedAtLeast5Answers()
    {
        var history = new History().Exercise(1);
        for (var s = 0; s < 30; s++) history.At(Start.AddDays(s)).Answer(1, Ones(4));

        history.Evaluate().Should().NotContain(BadgeKeys.FaithfulPractitioner);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void Sessions_SplitOnBreaksLongerThan30Minutes(int breakMinutes, bool earned)
    {
        var history = new History().Exercise(1).Answer(1, Ones(5));
        for (var s = 1; s < 30; s++) history.Pause(TimeSpan.FromMinutes(breakMinutes)).Answer(1, Ones(5));

        history.Evaluate().Contains(BadgeKeys.FaithfulPractitioner).Should().Be(earned);
    }

    [Theory]
    [InlineData(18, true)]
    [InlineData(19, false)]
    public void TenSessionsInAWeek_CountsSessionsStartingWithin7Days(int hoursApart, bool earned)
    {
        // Nine gaps of 18 h span 162 h (under a week); of 19 h, 171 h.
        var history = new History().Exercise(1);
        for (var s = 0; s < 10; s++) history.At(Start.AddHours(s * hoursApart)).Answer(1, Ones(5));

        history.Evaluate().Contains(BadgeKeys.TenSessionsWeek).Should().Be(earned);
    }

    [Theory]
    [InlineData("ChordRecognition", 3, 18, true)]
    [InlineData("ChordRecognition", 3, 17, false)]
    [InlineData("ChordRecognition", 2, 20, false)]
    [InlineData("IntervalRecognition", 3, 20, false)]
    public void MasterChords_Needs18Of20InThreeChordExercises(string type, int exercises, int rightOf20, bool earned)
    {
        var history = new History();
        for (var id = 1; id <= exercises; id++) history.Exercise(id, type: type).Answer(id, Zeros(20 - rightOf20) + Ones(rightOf20));

        history.Evaluate().Contains(BadgeKeys.MasterChords).Should().Be(earned);
    }

    [Theory]
    [InlineData("EarTraining", 3, 18, true)]
    [InlineData("EarTraining", 3, 17, false)]
    [InlineData("EarTraining", 2, 20, false)]
    [InlineData("Harmony", 3, 20, false)]
    public void SharpListener_Needs18Of20InThreeEarTrainingExercises(string category, int exercises, int rightOf20, bool earned)
    {
        var history = new History();
        for (var id = 1; id <= exercises; id++) history.Exercise(id, category: category).Answer(id, Ones(rightOf20) + Zeros(20 - rightOf20));

        history.Evaluate().Contains(BadgeKeys.SharpListener).Should().Be(earned);
    }

    [Fact]
    public void RhythmMaestro_NeedsTwoPerfectRhythmSessions()
    {
        var history = new History().Exercise(1, category: "Rhythm").Exercise(2, category: "Rhythm")
            .Answer(1, "11111")
            .Pause(Hour).Answer(2, "111111");

        history.Evaluate().Should().Contain(BadgeKeys.RhythmMaestro);
    }

    [Theory]
    [InlineData("11110")]
    [InlineData("1111")]
    public void RhythmMaestro_IgnoresSessionsWithMistakesOrTooShort(string secondSession)
    {
        var history = new History().Exercise(1, category: "Rhythm")
            .Answer(1, "11111")
            .Pause(Hour).Answer(1, secondSession);

        history.Evaluate().Should().NotContain(BadgeKeys.RhythmMaestro);
    }

    [Fact]
    public void MelodyExplorer_Needs8Of10InEveryMelodyExercise()
    {
        var history = new History().Exercise(1, category: "Melody").Exercise(2, category: "Melody")
            .Answer(1, "1111111100");
        history.Evaluate().Should().NotContain(BadgeKeys.MelodyExplorer, "exercise 2 was never practiced");

        history.Answer(2, "0111111110");
        history.Evaluate().Should().Contain(BadgeKeys.MelodyExplorer);
    }

    [Fact]
    public void ScaleClimber_Needs5RightAnswersInEveryScaleExercise()
    {
        var history = new History()
            .Exercise(1, type: "ScaleRecognition").Exercise(2, type: "ScaleRecognition")
            .Answer(1, "1111100").Answer(2, "101011");
        history.Evaluate().Should().NotContain(BadgeKeys.ScaleClimber, "exercise 2 has 4 right answers");

        history.Answer(2, "1");
        history.Evaluate().Should().Contain(BadgeKeys.ScaleClimber);
    }

    [Theory]
    [InlineData("00011" + "1111111100", true)]
    [InlineData("00111" + "1111111100", false)]
    [InlineData("00011" + "1111111000", false)]
    [InlineData("00011" + "111111110", false)]
    public void ComebackKid_MissesTheStartThenGets8Of10(string results, bool earned)
    {
        new History().Exercise(1).Answer(1, results).Evaluate()
            .Contains(BadgeKeys.ComebackKid).Should().Be(earned);
    }

    [Theory]
    [InlineData(5, "1111111000", true)]
    [InlineData(5, "1111110000", false)]
    [InlineData(4, "1111111111", false)]
    public void AdvancedConqueror_Needs7Of10InFiveAdvancedExercises(int exercises, string results, bool earned)
    {
        var history = new History();
        for (var id = 1; id <= exercises; id++) history.Exercise(id, difficulty: "Advanced").Answer(id, results);

        history.Evaluate().Contains(BadgeKeys.AdvancedConqueror).Should().Be(earned);
    }

    [Fact]
    public void PersistentStudent_NeedsAccuracyRisingFourSessionsInARow()
    {
        var history = new History().Exercise(1)
            .Answer(1, "10000")
            .Pause(Hour).Answer(1, "11000")
            .Pause(Hour).Answer(1, "11100");
        history.Evaluate().Should().NotContain(BadgeKeys.PersistentStudent, "accuracy rose only twice");

        history.Pause(Hour).Answer(1, "11110");
        history.Evaluate().Should().Contain(BadgeKeys.PersistentStudent);
    }

    [Fact]
    public void PersistentStudent_StartsOverWhenAccuracyStalls()
    {
        var history = new History().Exercise(1);
        foreach (var session in new[] { "10000", "11000", "11000", "11100", "11110" })
        {
            history.Pause(Hour).Answer(1, session);
        }

        history.Evaluate().Should().NotContain(BadgeKeys.PersistentStudent);
    }

    [Fact]
    public void NotableProgress_EveryCategoryImprovesOverThePrevious30Days()
    {
        var history = ProgressHistory(recentEarTraining: "1111111000", recentRhythm: "1111000000");

        history.Evaluate().Should().Contain(BadgeKeys.NotableProgress);
    }

    [Fact]
    public void NotableProgress_FailsWhenACategoryGetsWorse()
    {
        var history = ProgressHistory(recentEarTraining: "1111111000", recentRhythm: "1100000000");

        history.Evaluate().Should().NotContain(BadgeKeys.NotableProgress);
    }

    [Fact]
    public void NotableProgress_NeedsTwoCategoriesWith10AnswersInBothPeriods()
    {
        var history = ProgressHistory(recentEarTraining: "1111111000", recentRhythm: "111100000");

        history.Evaluate().Should().NotContain(BadgeKeys.NotableProgress, "Rhythm has only 9 recent answers");
    }

    [Theory]
    [InlineData("0001", true)]
    [InlineData("0010001", true)]
    [InlineData("001", false)]
    [InlineData("00100", false)]
    public void ResilientEar_RightAnswerAfterThreeMissesInARow(string results, bool earned)
    {
        new History().Exercise(1).Answer(1, results).Evaluate()
            .Contains(BadgeKeys.ResilientEar).Should().Be(earned);
    }

    [Fact]
    public void ResilientEar_CountsMissesInTheSameExercise()
    {
        new History().Exercise(1).Exercise(2).Answer(1, "000").Answer(2, "1").Evaluate()
            .Should().NotContain(BadgeKeys.ResilientEar);
    }

    [Theory]
    [InlineData(10, "11110", true)]
    [InlineData(9, "11111", false)]
    [InlineData(10, "11100", false)]
    public void IntervalTamer_Needs10IntervalSessionsAt80Percent(int sessions, string session, bool earned)
    {
        var history = new History().Exercise(1, type: "IntervalRecognition");
        for (var s = 0; s < sessions; s++) history.Pause(Hour).Answer(1, session);

        history.Evaluate().Contains(BadgeKeys.IntervalTamer).Should().Be(earned);
    }

    [Fact]
    public void BadgeCollector_Needs15OtherAvailableBadges()
    {
        var others = OtherAvailableBadges();
        var history = new History().Exercise(1).Answer(1, "1");

        // The collector badge is checked last, so the badge that completes the set counts.
        history.Evaluate(others.Take(14).ToArray()).Should().Equal(BadgeKeys.FirstSession, BadgeKeys.BadgeCollector);
        history.Evaluate(others.Take(13).ToArray()).Should().Equal(BadgeKeys.FirstSession);
    }

    [Fact]
    public void BadgeCollector_IgnoresBadgesThatAreNotAvailable()
    {
        var unavailable = BadgeCatalog.All.Where(b => !b.IsAvailable).Select(b => b.Key);
        var history = new History().Exercise(1).Answer(1, "1");

        history.Evaluate(OtherAvailableBadges().Take(13).Concat(unavailable).ToArray())
            .Should().Equal(BadgeKeys.FirstSession);
    }

    private static TimeZoneInfo Toronto => TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    private static string Ones(int count) => new('1', count);

    private static string Zeros(int count) => new('0', count);

    private static string[] OtherAvailableBadges() => BadgeCatalog.Available
        .Select(b => b.Key)
        .Where(key => key is not BadgeKeys.FirstSession and not BadgeKeys.BadgeCollector)
        .ToArray();

    // Ear training 50% and rhythm 30% in the previous 30 days; the recent results vary.
    private static History ProgressHistory(string recentEarTraining, string recentRhythm)
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        return new History { Now = now }
            .Exercise(1, category: "EarTraining").Exercise(2, category: "Rhythm")
            .At(now.AddDays(-45)).Answer(1, "1111100000").Answer(2, "1110000000")
            .At(now.AddDays(-10)).Answer(1, recentEarTraining).Answer(2, recentRhythm);
    }

    private sealed class History
    {
        private readonly List<PracticeAnswer> _answers = [];
        private readonly Dictionary<int, ExerciseInfo> _exercises = [];
        private DateTime _clock = Start;

        /// <summary>Defaults to an hour after the last answer.</summary>
        public DateTime? Now { get; init; }

        public History Exercise(int id, string type = "", string category = "", string difficulty = "")
        {
            _exercises[id] = new ExerciseInfo(id, type, category, difficulty);
            return this;
        }

        public History At(DateTime utc)
        {
            _clock = utc;
            return this;
        }

        /// <summary>The next answer comes <paramref name="gap"/> after the previous one.</summary>
        public History Pause(TimeSpan gap)
        {
            _clock = (_answers.Count > 0 ? _answers[^1].Timestamp : _clock) + gap;
            return this;
        }

        public History Answer(int exerciseId, string results)
        {
            foreach (var result in results)
            {
                _answers.Add(new PracticeAnswer(exerciseId, result == '1', _clock));
                _clock = _clock.AddMinutes(1);
            }
            return this;
        }

        public IReadOnlyList<string> Evaluate(params string[] alreadyEarned) => EvaluateIn(TimeZoneInfo.Utc, alreadyEarned);

        public IReadOnlyList<string> EvaluateIn(TimeZoneInfo timeZone, params string[] alreadyEarned)
        {
            var ordered = _answers.OrderBy(a => a.Timestamp).ToList();
            var now = Now ?? (ordered.Count > 0 ? ordered[^1].Timestamp + Hour : Start);
            return BadgeRules.Evaluate(ordered, _exercises, alreadyEarned.ToHashSet(StringComparer.Ordinal), timeZone, now);
        }
    }
}
