using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Every badge rule, with the smallest history that earns the badge and one
/// that just misses it. Answers are one minute apart unless a test moves the
/// clock; results are written as strings, "1" for right and "0" for wrong.
/// Hidden badges are never awarded, so their rules are checked one by one with Earns.
/// </summary>
public class BadgeRulesTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FirstDay = DateOnly.FromDateTime(Start);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly Lazy<IReadOnlyList<ExerciseInfo>> SeededExercises = new(LoadSeededExercises);

    // An exercise with two filter groups and the preset of an answer played with another key.
    private static readonly Dictionary<string, string> KeyAndScale = new() { ["keySelect"] = "C", ["scaleSelect"] = "major" };
    private const string InD = """{"keySelect":"D","scaleSelect":"major"}""";

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

    [Fact]
    public void BadgeCollectorThreshold_CanBeReachedWithTheAvailableBadges()
    {
        BadgeCatalog.Available.Count(b => b.Key != BadgeKeys.BadgeCollector)
            .Should().BeGreaterThanOrEqualTo(BadgeRules.BadgeCollectorThreshold);
    }

    [Fact]
    public void HiddenBadges_AreNeverAwarded_EvenWhenTheirRuleHolds()
    {
        var history = new History().Exercise(1).At(Start.Date.AddHours(23)).Answer(1, Ones(10));

        history.Earns(BadgeKeys.PerfectSession).Should().BeTrue();
        history.Earns(BadgeKeys.NightOwl).Should().BeTrue();
        history.Evaluate().Should().OnlyContain(key => BadgeCatalog.Find(key)!.IsAvailable);
    }

    [Theory]
    [InlineData(6, false, false)]
    [InlineData(7, true, false)]
    [InlineData(29, true, false)]
    [InlineData(30, true, true)]
    public void LongStreaks_Need7And30DaysInARow(int days, bool sevenDays, bool thirtyDays)
    {
        var history = new History().Exercise(1);
        for (var day = 0; day < days; day++) history.At(Start.AddDays(day)).Answer(1, "1");

        history.Earns(BadgeKeys.SevenDays).Should().Be(sevenDays);
        history.Earns(BadgeKeys.ThirtyDays).Should().Be(thirtyDays);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void HundredSessions_Needs100Sessions(int sessions, bool earned)
    {
        var history = new History().Exercise(1);
        for (var s = 0; s < sessions; s++) history.Pause(Hour).Answer(1, "00000");
        history.Pause(Hour).Answer(1, "1111");

        history.Earns(BadgeKeys.HundredSessions).Should().Be(earned);
    }

    [Theory]
    [InlineData("1111111111", true)]
    [InlineData("111111111", false)]
    [InlineData("1111111110", false)]
    [InlineData("0111111111111", false)]
    public void PerfectSession_NeedsASessionOfAtLeast10Answers_AllRight(string session, bool earned)
    {
        new History().Exercise(1).Answer(1, session).Earns(BadgeKeys.PerfectSession).Should().Be(earned);
    }

    [Fact]
    public void PerfectSession_TakesTheWholeSession_AcrossExercises()
    {
        new History().Exercise(1).Exercise(2).Answer(1, "11111").Answer(2, "11111")
            .Earns(BadgeKeys.PerfectSession).Should().BeTrue();
        new History().Exercise(1).Answer(1, "11111").Pause(Hour).Answer(1, "11111")
            .Earns(BadgeKeys.PerfectSession).Should().BeFalse("two sessions of 5 answers are not one of 10");
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(9, false)]
    public void AllRounder_Needs10AnswersInEveryCategory_RightOrWrong(int rhythmAnswers, bool earned)
    {
        new History()
            .Exercise(1, category: "Harmony").Exercise(2, category: "Rhythm").Exercise(3, category: "Rhythm")
            .Answer(1, Zeros(10)).Answer(2, "11111").Answer(3, Zeros(rhythmAnswers - 5))
            .Earns(BadgeKeys.AllRounder).Should().Be(earned);
    }

    [Fact]
    public void AllRounder_SkipsCategoriesOfMicrophoneExercisesOnly_ButCountsTheirAnswers()
    {
        new History()
            .Exercise(1, category: "Harmony")
            .Exercise(2, category: "Melody", name: "IntervalMelodico")
            .Exercise(3, category: "Melody", name: "SolfegeMelody")
            .Answer(1, Ones(10)).Answer(2, "11111").Answer(3, "11111")
            .Earns(BadgeKeys.AllRounder).Should().BeTrue("Solfege answers count for the Melody category");
        new History()
            .Exercise(1, category: "Harmony").Exercise(2, category: "Singing", name: "SolfegeMelody")
            .Answer(1, Ones(10))
            .Earns(BadgeKeys.AllRounder).Should().BeTrue("a category of microphone exercises only is not required");
        new History().Exercise(1).Answer(1, Ones(10))
            .Earns(BadgeKeys.AllRounder).Should().BeFalse("there is no category to cover");
    }

    [Theory]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]
    [InlineData(4, 59, true)]
    [InlineData(5, 0, false)]
    public void NightOwl_StartsASessionBetween10pmAnd5am(int hour, int minute, bool earned)
    {
        new History().Exercise(1).At(Start.Date.AddHours(hour).AddMinutes(minute)).Answer(1, "11111")
            .Earns(BadgeKeys.NightOwl).Should().Be(earned);
    }

    [Theory]
    [InlineData(4, 59, false)]
    [InlineData(5, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    public void EarlyBird_StartsASessionBetween5And7am(int hour, int minute, bool earned)
    {
        new History().Exercise(1).At(Start.Date.AddHours(hour).AddMinutes(minute)).Answer(1, "11111")
            .Earns(BadgeKeys.EarlyBird).Should().Be(earned);
    }

    [Fact]
    public void NightOwl_ReadsThePlayersClock_AndNeedsASession()
    {
        // 23:30 UTC is 19:30 in Toronto, and 02:30 UTC is 22:30 there the evening before.
        var evening = new History().Exercise(1).At(Start.Date.AddHours(23.5)).Answer(1, "11111");
        evening.Earns(BadgeKeys.NightOwl).Should().BeTrue();
        evening.EarnsIn(Toronto, BadgeKeys.NightOwl).Should().BeFalse();
        new History().Exercise(1).At(Start.Date.AddHours(26.5)).Answer(1, "11111")
            .EarnsIn(Toronto, BadgeKeys.NightOwl).Should().BeTrue();

        new History().Exercise(1).At(Start.Date.AddHours(23)).Answer(1, "1111")
            .Earns(BadgeKeys.NightOwl).Should().BeFalse("four answers are a warm-up, not a session");
    }

    [Fact]
    public void Explorer_NeedsAnAnswerToEveryExercise_ButTheSingingOnes()
    {
        var names = SeededExercises.Value.Select(e => e.Name).Where(n => !MicrophoneExercises.Contains(n)).Distinct().ToList();
        names.Should().NotContain(["SolfegeMelody", "SingNote", "SingInterval", "SingMelody"]);

        var everyExercise = new History().Seeded();
        foreach (var name in names) everyExercise.Answer(IdOf(name), "0");
        everyExercise.Earns(BadgeKeys.Explorer).Should().BeTrue();

        var oneMissing = new History().Seeded();
        foreach (var name in names.Skip(1)) oneMissing.Answer(IdOf(name), "1");
        oneMissing.Earns(BadgeKeys.Explorer).Should().BeFalse();
    }

    [Fact]
    public void Explorer_CountsAnswersSavedUnderAnyRowOfTheName()
    {
        new History().Exercise(1, name: "GuessNote").Exercise(2, name: "GuessNote").Answer(2, "1")
            .Earns(BadgeKeys.Explorer).Should().BeTrue();
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void FilterNinja_Needs5SessionsPlayedWithFiltersOtherThanTheDefaults(int sessions, bool earned)
    {
        var history = new History().Exercise(1, filters: KeyAndScale);
        // One answer with other filters is enough for its session.
        for (var s = 0; s < sessions; s++) history.Pause(Hour).Answer(1, "1111").Answer(1, "1", InD);

        history.Earns(BadgeKeys.FilterNinja).Should().Be(earned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"keySelect":"C","scaleSelect":"major"}""")]
    [InlineData("""{"tempo":"fast"}""")]
    [InlineData("not json")]
    public void FilterNinja_IgnoresTheDefaults_AndFiltersTheExerciseDoesNotHave(string? filterJson)
    {
        var history = new History().Exercise(1, filters: KeyAndScale);
        for (var s = 0; s < 5; s++) history.Pause(Hour).Answer(1, "11111", filterJson);

        history.Earns(BadgeKeys.FilterNinja).Should().BeFalse();
    }

    [Fact]
    public void FilterNinja_NeedsSessions_OnKnownExercises()
    {
        var warmUps = new History().Exercise(1, filters: KeyAndScale);
        for (var s = 0; s < 5; s++) warmUps.Pause(Hour).Answer(1, "1111", InD);
        warmUps.Earns(BadgeKeys.FilterNinja).Should().BeFalse("four answers are a warm-up, not a session");

        var unknown = new History();
        for (var s = 0; s < 5; s++) unknown.Pause(Hour).Answer(42, "11111", InD);
        unknown.Earns(BadgeKeys.FilterNinja).Should().BeFalse();
    }

    [Fact]
    public void FilterNinja_ComparesWithTheFirstOptionOfTheSeededFilters()
    {
        var completeScale = IdOf("CompleteScale");
        var defaults = new History().Seeded();
        var otherOctave = new History().Seeded();
        for (var s = 0; s < 5; s++)
        {
            defaults.Pause(Hour).Answer(completeScale, "11111", """{"csOctave":"3","csRoot":"any","csScale":"all"}""");
            otherOctave.Pause(Hour).Answer(completeScale, "11111", """{"csOctave":"4","csRoot":"any","csScale":"all"}""");
        }

        defaults.Earns(BadgeKeys.FilterNinja).Should().BeFalse();
        otherOctave.Earns(BadgeKeys.FilterNinja).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(9, true, false)]
    [InlineData(10, true, true)]
    public void ChallengeBadges_CountTheDaysWhoseChallengeWasCompleted(int days, bool one, bool ten)
    {
        var history = new History().Seeded();
        for (var day = 0; day < days; day++) history.AnswerChallenge(FirstDay.AddDays(day));

        history.Earns(BadgeKeys.DailyChallengeComplete).Should().Be(one);
        history.Earns(BadgeKeys.MissionAddict).Should().Be(ten);
    }

    [Fact]
    public void ChallengeBadges_SkipADayWithOneAnswerMissing()
    {
        new History().Seeded().AnswerChallenge(FirstDay, missing: 1)
            .Earns(BadgeKeys.DailyChallengeComplete).Should().BeFalse();

        var history = new History().Seeded();
        for (var day = 0; day < 10; day++) history.AnswerChallenge(FirstDay.AddDays(day), missing: day == 9 ? 1 : 0);
        history.Earns(BadgeKeys.MissionAddict).Should().BeFalse();
    }

    [Fact]
    public void TotalMastery_NeedsEveryStepOfTheLearningPath()
    {
        var steps = LearningPathCatalog.Steps;

        var complete = new History().Seeded();
        foreach (var step in steps) complete.Answer(IdOf(step.Exercise), Ones(step.Required));
        complete.Earns(BadgeKeys.TotalMastery).Should().BeTrue();

        var lastStepMissing = new History().Seeded();
        foreach (var step in steps.SkipLast(1)) lastStepMissing.Answer(IdOf(step.Exercise), Ones(step.Required));
        lastStepMissing.Earns(BadgeKeys.TotalMastery).Should().BeFalse();

        new History().Exercise(1).Answer(1, Ones(10))
            .Earns(BadgeKeys.TotalMastery).Should().BeFalse("there is no path without its exercises");
    }

    [Theory]
    [InlineData(12, 0, true)]
    [InlineData(12, 2, true)]
    [InlineData(12, 3, false)]
    [InlineData(13, 0, false)]
    public void Speedster_Needs18Of20AnswersRight_Within4Minutes(int seconds, int wrong, bool earned)
    {
        // 20 answers make 19 gaps: 3:48 at 12 seconds, 4:07 at 13.
        new History().Exercise(1).Exercise(2).Every(TimeSpan.FromSeconds(seconds))
            .Answer(1, Zeros(wrong) + Ones(10 - wrong)).Answer(2, Ones(10))
            .Earns(BadgeKeys.Speedster).Should().Be(earned);
    }

    [Theory]
    [InlineData("11111", true)]
    [InlineData("11110111", false)]
    public void MysteryListener_Needs5GuessNoteAnswersRightInARow(string results, bool earned)
    {
        new History().Exercise(1, name: "GuessNote").Answer(1, results)
            .Earns(BadgeKeys.MysteryListener).Should().Be(earned);
    }

    [Fact]
    public void MysteryListener_CountsEveryGuessNoteRow_AndOnlyThem()
    {
        new History().Exercise(1, name: "GuessNote").Exercise(2, name: "GuessNote").Exercise(3, name: "GuessChords")
            .Answer(1, "111").Answer(3, "00").Answer(2, "11")
            .Earns(BadgeKeys.MysteryListener).Should().BeTrue();
        new History().Exercise(3, name: "GuessChords").Answer(3, "11111")
            .Earns(BadgeKeys.MysteryListener).Should().BeFalse();
    }

    [Theory]
    [InlineData("111", true)]
    [InlineData("1101", false)]
    public void ImpossibleMelody_Needs3MelodicDictationsRightInARow(string results, bool earned)
    {
        new History().Exercise(1, name: "MelodicDictation").Answer(1, results)
            .Earns(BadgeKeys.ImpossibleMelody).Should().Be(earned);
    }

    private static TimeZoneInfo Toronto => TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    private static string Ones(int count) => new('1', count);

    private static string Zeros(int count) => new('0', count);

    private static string[] OtherAvailableBadges() => BadgeCatalog.Available
        .Select(b => b.Key)
        .Where(key => key is not BadgeKeys.FirstSession and not BadgeKeys.BadgeCollector)
        .ToArray();

    private static int IdOf(string exerciseName) =>
        SeededExercises.Value.Where(e => e.Name == exerciseName).Min(e => e.ExerciseId);

    private static IReadOnlyList<ExerciseInfo> LoadSeededExercises()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"badge-rules-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        return db.Exercises
            .Select(e => new
            {
                e.ExerciseId,
                e.Name,
                Type = e.ExerciseType != null ? e.ExerciseType.Name : "",
                Category = e.ExerciseCategory != null ? e.ExerciseCategory.Name : "",
                Difficulty = e.DifficultyLevel != null ? e.DifficultyLevel.Name : "",
                e.FiltersJson,
            })
            .AsEnumerable()
            .Select(e => new ExerciseInfo(e.ExerciseId, e.Name, e.Type, e.Category, e.Difficulty,
                ExerciseFilterPresets.Defaults(ExerciseFilterPresets.Groups(e.FiltersJson))))
            .ToList();
    }

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
        private TimeSpan _step = TimeSpan.FromMinutes(1);

        /// <summary>Defaults to an hour after the last answer.</summary>
        public DateTime? Now { get; init; }

        /// <param name="name">Defaults to "Exercise{id}".</param>
        /// <param name="filters">Filter group → its first option; none by default.</param>
        public History Exercise(
            int id,
            string type = "",
            string category = "",
            string difficulty = "",
            string? name = null,
            IReadOnlyDictionary<string, string>? filters = null)
        {
            _exercises[id] = new ExerciseInfo(
                id, name ?? $"Exercise{id}", type, category, difficulty, filters ?? new Dictionary<string, string>());
            return this;
        }

        /// <summary>Adds every seeded exercise, with its real name, taxonomy and filters.</summary>
        public History Seeded()
        {
            foreach (var exercise in SeededExercises.Value) _exercises[exercise.ExerciseId] = exercise;
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

        /// <summary>Spaces the next answers <paramref name="step"/> apart instead of a minute.</summary>
        public History Every(TimeSpan step)
        {
            _step = step;
            return this;
        }

        public History Answer(int exerciseId, string results, string? filterJson = null)
        {
            foreach (var result in results)
            {
                _answers.Add(new PracticeAnswer(exerciseId, result == '1', _clock, filterJson));
                _clock += _step;
            }
            return this;
        }

        /// <summary>
        /// From noon UTC on <paramref name="date"/>, answers each exercise of that day's challenge as
        /// many times as it asks, <paramref name="missing"/> answers short on the last one.
        /// </summary>
        public History AnswerChallenge(DateOnly date, int missing = 0)
        {
            var picked = DailyChallengeRules.Pick(
                date, _exercises.Values.Select(e => new ChallengeExercise(e.ExerciseId, e.Name, e.Category)));
            At(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
            for (var i = 0; i < picked.Count; i++)
            {
                var answers = DailyChallengeRules.Target(picked[i].Name) - (i == picked.Count - 1 ? missing : 0);
                Answer(picked[i].ExerciseId, Zeros(answers));
            }
            return this;
        }

        public IReadOnlyList<string> Evaluate(params string[] alreadyEarned) => EvaluateIn(TimeZoneInfo.Utc, alreadyEarned);

        public IReadOnlyList<string> EvaluateIn(TimeZoneInfo timeZone, params string[] alreadyEarned)
        {
            var ordered = Ordered();
            return BadgeRules.Evaluate(ordered, _exercises, alreadyEarned.ToHashSet(StringComparer.Ordinal), timeZone, NowAfter(ordered));
        }

        /// <summary>Whether the rule of <paramref name="key"/> holds, even for a hidden badge that Evaluate never awards.</summary>
        public bool Earns(string key) => EarnsIn(TimeZoneInfo.Utc, key);

        public bool EarnsIn(TimeZoneInfo timeZone, string key)
        {
            var ordered = Ordered();
            return BadgeRules.IsEarned(key, ordered, _exercises, timeZone, NowAfter(ordered));
        }

        private List<PracticeAnswer> Ordered() => _answers.OrderBy(a => a.Timestamp).ToList();

        private DateTime NowAfter(List<PracticeAnswer> ordered) =>
            Now ?? (ordered.Count > 0 ? ordered[^1].Timestamp + Hour : Start);
    }
}
