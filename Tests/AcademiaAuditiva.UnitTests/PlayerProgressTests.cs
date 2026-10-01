using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.UnitTests;

/// <summary>XP, levels and ranks, practice streaks, and the session helpers behind the badge rules.</summary>
public class PlayerProgressTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(3, 300)]
    [InlineData(4, 600)]
    [InlineData(5, 1000)]
    public void XpForLevel_EachLevelTakes100XpMoreThanThePreviousOne(int level, int totalXp)
    {
        Leveling.XpForLevel(level).Should().Be(totalXp);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    [InlineData(int.MaxValue, Leveling.MaxLevel)]
    public void LevelForXp_IsTheHighestLevelReached(int xp, int level)
    {
        Leveling.LevelForXp(xp).Should().Be(level);
    }

    [Theory]
    [InlineData(1, "pp")]
    [InlineData(2, "pp")]
    [InlineData(3, "p")]
    [InlineData(4, "p")]
    [InlineData(5, "mp")]
    [InlineData(6, "mp")]
    [InlineData(7, "mf")]
    [InlineData(9, "mf")]
    [InlineData(10, "f")]
    [InlineData(14, "f")]
    [InlineData(15, "ff")]
    [InlineData(19, "ff")]
    [InlineData(20, "fff")]
    [InlineData(Leveling.MaxLevel, "fff")]
    public void Ranks_FollowDynamicsFromPianissimoToFortississimo(int level, string rank)
    {
        Leveling.RankForLevel(level).Should().Be(rank);
        Leveling.Ranks.Should().Contain(rank);
    }

    [Fact]
    public void Progress_AddsXpForRightAndWrongAnswersAndBadges()
    {
        var progress = PlayerProgress.From(correctAnswers: 8, totalAnswers: 10, badges: 1, streak: default);

        progress.Xp.Should().Be(8 * Leveling.XpPerCorrectAnswer + 2 * Leveling.XpPerWrongAnswer + Leveling.XpPerBadge);
        progress.Xp.Should().Be(134);
        progress.Level.Should().Be(2);
        progress.Rank.Should().Be("pp");
        progress.LevelStartXp.Should().Be(100);
        progress.NextLevelXp.Should().Be(300);
        progress.XpToNextLevel.Should().Be(166);
        progress.LevelPercent.Should().Be(17);
    }

    [Fact]
    public void Progress_NewPlayerStartsAtLevel1()
    {
        var progress = PlayerProgress.From(0, 0, 0, default);

        progress.Xp.Should().Be(0);
        progress.Level.Should().Be(1);
        progress.LevelPercent.Should().Be(0);
        progress.XpToNextLevel.Should().Be(100);
    }

    [Fact]
    public void Streak_IsEmptyWithoutAnswers()
    {
        PracticeStreak.Compute([], TimeZoneInfo.Utc, Now).Should().Be(new StreakInfo(0, 0, false));
    }

    [Fact]
    public void Streak_CountsTodayAndTheDaysBefore()
    {
        Streak(Now, Now.AddHours(-1), Now.AddDays(-1), Now.AddDays(-2))
            .Should().Be(new StreakInfo(Current: 3, Best: 3, PracticedToday: true));
    }

    [Fact]
    public void Streak_StaysAliveUntilTheEndOfToday()
    {
        Streak(Now.AddDays(-1), Now.AddDays(-2))
            .Should().Be(new StreakInfo(Current: 2, Best: 2, PracticedToday: false));
    }

    [Fact]
    public void Streak_EndsAfterAMissedDay_ButTheBestRunIsKept()
    {
        Streak(Now.AddDays(-2), Now.AddDays(-3))
            .Should().Be(new StreakInfo(Current: 0, Best: 2, PracticedToday: false));
        Streak(Now, Now.AddDays(-20), Now.AddDays(-19), Now.AddDays(-18), Now.AddDays(-17))
            .Should().Be(new StreakInfo(Current: 1, Best: 4, PracticedToday: true));
    }

    [Fact]
    public void Streak_FollowsThePlayersCalendar()
    {
        // 03:00 UTC on Oct 1 is 23:00 on Sep 30 in Toronto, where it is now 11:00 on Oct 1.
        var lateEvening = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

        PracticeStreak.Compute([lateEvening], TimeZoneInfo.Utc, Now)
            .Should().Be(new StreakInfo(Current: 1, Best: 1, PracticedToday: true));
        PracticeStreak.Compute([lateEvening], toronto, Now)
            .Should().Be(new StreakInfo(Current: 1, Best: 1, PracticedToday: false));
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    public void Sessions_SplitOnBreaksLongerThan30Minutes(int breakMinutes, int sessions)
    {
        var start = Now.AddHours(-3);
        var answers = Enumerable.Range(0, 5).Select(i => new PracticeAnswer(1, true, start.AddMinutes(i)))
            .Concat(Enumerable.Range(0, 5).Select(i => new PracticeAnswer(1, true, start.AddMinutes(4 + breakMinutes + i))))
            .ToList();

        PracticeSessions.Counted(answers).Should().HaveCount(sessions);
    }

    [Theory]
    [InlineData("11111111", 10, 8, false)]
    [InlineData("1111111100", 10, 8, true)]
    [InlineData("0111111110", 10, 9, false)]
    [InlineData("0001111111111", 10, 10, true)]
    [InlineData("", 1, 1, false)]
    public void HasWindow_LooksForEnoughRightAnswersInARow(string results, int size, int minCorrect, bool expected)
    {
        PracticeSessions.HasWindow(results.Select(c => c == '1').ToList(), size, minCorrect).Should().Be(expected);
    }

    [Theory]
    [InlineData("11110", 80, true)]
    [InlineData("1110", 80, false)]
    [InlineData("", 0, false)]
    public void AccuracyAtLeast_UsesExactPercentages(string results, int percent, bool expected)
    {
        var answers = results.Select((c, i) => new PracticeAnswer(1, c == '1', Now.AddMinutes(i))).ToList();

        PracticeSessions.AccuracyAtLeast(answers, percent).Should().Be(expected);
    }

    private static StreakInfo Streak(params DateTime[] answers) => PracticeStreak.Compute(answers, TimeZoneInfo.Utc, Now);
}
