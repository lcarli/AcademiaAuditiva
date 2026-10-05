using AcademiaAuditiva.Services.Dashboard;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The student dashboard's figures, worked out from the answers and the totals per exercise.</summary>
public class DashboardStatsTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly DateTime Start = new(2026, 1, 10, 14, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33.3)]
    [InlineData(2, 3, 66.7)]
    [InlineData(4, 4, 100)]
    public void Percent_HasOneDecimal_AndIsZeroWithoutAnswers(int part, int whole, double expected) =>
        DashboardStats.Percent(part, whole).Should().Be(expected);

    [Fact]
    public void Timeline_CountsEachDayOfTheStudentsCalendar_OldestFirst()
    {
        var answers = new[]
        {
            // 22:30 on January 11 in Toronto, already January 12 in UTC.
            Answer("GuessNote", true, new DateTime(2026, 1, 12, 3, 30, 0, DateTimeKind.Utc)),
            Answer("GuessNote", false, new DateTime(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc)),
            Answer("GuessNote", true, Start),
            Answer("GuessInterval", true, Start.AddMinutes(5)),
            Answer("GuessInterval", false, Start.AddMinutes(6)),
        };

        DashboardStats.Timeline(answers, Toronto).Should().Equal(
            new TimelineDay("2026-01-10", 3, 66.7),
            new TimelineDay("2026-01-11", 1, 100),
            new TimelineDay("2026-01-12", 1, 0));
    }

    [Fact]
    public void Timeline_ShowsTheLatestDaysWithAnswers()
    {
        // Every other day, 40 times.
        var answers = Enumerable.Range(0, 40).Select(i => Answer("GuessNote", true, Start.AddDays(2 * i)));

        var timeline = DashboardStats.Timeline(answers, TimeZoneInfo.Utc);

        timeline.Should().HaveCount(DashboardStats.TimelineDays);
        timeline[0].Date.Should().Be("2026-01-30");
        timeline[^1].Date.Should().Be("2026-03-29");
        timeline.Select(d => d.Date).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void RecentSessions_SplitEachExercisesAnswersAtPausesOverHalfAnHour()
    {
        var answers = new[]
        {
            Answer("GuessNote", true, Start, seconds: 10),
            Answer("GuessNote", false, Start.AddMinutes(30), seconds: 20),
            Answer("GuessNote", true, Start.AddMinutes(61), seconds: 30),
            // Played in between, but a session of its own.
            Answer("GuessInterval", true, Start.AddMinutes(5), seconds: 7),
        };

        DashboardStats.RecentSessions(answers).Should().Equal(
            new SessionSummary("GuessNote", Start.AddMinutes(61), Start.AddMinutes(61), 1, 0, 30),
            new SessionSummary("GuessNote", Start, Start.AddMinutes(30), 1, 1, 30),
            new SessionSummary("GuessInterval", Start.AddMinutes(5), Start.AddMinutes(5), 1, 0, 7));
    }

    [Fact]
    public void RecentSessions_AreInUtc_EvenWhenTheDatabaseDoesNotSaySo()
    {
        // SQL Server returns the timestamps without a kind.
        var unspecified = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);

        var session = DashboardStats.RecentSessions([Answer("GuessNote", true, unspecified)]).Single();

        session.Start.Kind.Should().Be(DateTimeKind.Utc);
        session.End.Kind.Should().Be(DateTimeKind.Utc);
        session.Start.Should().Be(Start);
    }

    [Fact]
    public void RecentSessions_ShowsTheLatest()
    {
        var answers = Enumerable.Range(0, 15).Select(i => Answer("GuessNote", true, Start.AddHours(i)));

        var sessions = DashboardStats.RecentSessions(answers);

        sessions.Should().HaveCount(DashboardStats.SessionsShown);
        sessions[0].Start.Should().Be(Start.AddHours(14));
        sessions.Select(s => s.End).Should().BeInDescendingOrder();
    }

    [Fact]
    public void SessionScore_IsRightAnswersMinusWrongOnes() =>
        new SessionSummary("GuessNote", Start, Start, 3, 5, 0).Score.Should().Be(-2);

    [Fact]
    public void RecentForm_JudgesTheLatestAnswers_OnceThereAreEnough()
    {
        // The five oldest answers were wrong, the latest twenty right.
        var answers = Enumerable.Range(0, 25).Select(i => Answer("GuessNote", i >= 5, Start.AddMinutes(i)))
            .Concat(Enumerable.Range(0, DashboardStats.MinAnswersToJudge - 1).Select(i => Answer("GuessChords", false, Start.AddMinutes(i))))
            .Concat(Enumerable.Range(0, DashboardStats.MinAnswersToJudge).Select(i => Answer("GuessInterval", i == 0, Start.AddMinutes(i))));

        DashboardStats.RecentForm(answers).Should().BeEquivalentTo(new[]
        {
            new ExerciseForm("GuessNote", DashboardStats.RecentAnswers, 0),
            new ExerciseForm("GuessInterval", 5, 4),
        });
    }

    [Theory]
    [InlineData(20, 6, false)] // 70%
    [InlineData(20, 7, true)] // 65%
    [InlineData(3, 1, true)] // 66.7%
    public void IsBelow_ComparesTheShareOfRightAnswers(int answers, int errors, bool below) =>
        new ExerciseForm("GuessNote", answers, errors).IsBelow(70).Should().Be(below);

    [Fact]
    public void ErrorRate_IsAPercentage() =>
        new ExerciseForm("GuessNote", 20, 9).ErrorRate.Should().Be(45);

    [Fact]
    public void Struggles_PutTheLargestShareOfErrorsFirst_AndLeaveOutFlawlessExercises()
    {
        var form = new[]
        {
            new ExerciseForm("GuessNote", 20, 2),
            new ExerciseForm("GuessInterval", 20, 10),
            new ExerciseForm("GuessChords", 6, 3),
            new ExerciseForm("GuessQuality", 20, 0),
            new ExerciseForm("GuessFunction", 5, 4),
        };

        DashboardStats.Struggles(form).Select(f => f.Exercise).Should()
            .Equal("GuessFunction", "GuessInterval", "GuessChords", "GuessNote");
        DashboardStats.Struggles(form, take: 2).Select(f => f.Exercise).Should().Equal("GuessFunction", "GuessInterval");
    }

    [Fact]
    public void Recommend_KeepPracticing_WithoutEnoughAnswers() =>
        DashboardStats.Recommend([]).Should().Equal(new Recommendation(RecommendationKind.KeepPracticing));

    [Fact]
    public void Recommend_ReviewTheWeakestExercises()
    {
        var form = new[]
        {
            new ExerciseForm("GuessNote", 20, 2),
            new ExerciseForm("GuessInterval", 20, 7),
            new ExerciseForm("GuessChords", 20, 12),
            new ExerciseForm("GuessQuality", 20, 9),
            new ExerciseForm("GuessFunction", 20, 8),
            new ExerciseForm("GuessScaleType", 20, 6),
        };

        DashboardStats.Recommend(form).Should().Equal(
            new Recommendation(RecommendationKind.Review, "GuessChords"),
            new Recommendation(RecommendationKind.Review, "GuessQuality"),
            new Recommendation(RecommendationKind.Review, "GuessFunction"));
    }

    [Fact]
    public void Recommend_HarderExercises_WhenRightNineTimesOutOfTen() =>
        DashboardStats.Recommend([new ExerciseForm("GuessNote", 20, 1), new ExerciseForm("GuessInterval", 20, 3)])
            .Should().Equal(new Recommendation(RecommendationKind.IncreaseDifficulty));

    [Fact]
    public void Recommend_TheSameLevel_WhenNothingNeedsReviewButNotYetNineOutOfTen() =>
        DashboardStats.Recommend([new ExerciseForm("GuessNote", 20, 1), new ExerciseForm("GuessInterval", 20, 4)])
            .Should().Equal(new Recommendation(RecommendationKind.KeepCurrentLevel));

    [Fact]
    public void AccuracyBy_AddsUpEachGroup_InTheGivenOrder()
    {
        var totals = new[]
        {
            new ExerciseTotals("NoteRecognition", "EarTraining", "Intermediate", 2, 3, 1),
            new ExerciseTotals("IntervalRecognition", "EarTraining", "Beginner", 1, 1, 1),
            new ExerciseTotals("ChordRecognition", "Harmony", "Advanced", 3, 0, 4),
            new ExerciseTotals("ChordRecognition", "Harmony", "Beginner", 1, 2, 0),
            new ExerciseTotals("ScaleRecognition", "Scales", "Advanced", 3, 0, 0),
        };

        DashboardStats.AccuracyBy(totals, t => t.Category).Should().Equal(
            new GroupAccuracy("EarTraining", 66.7),
            new GroupAccuracy("Harmony", 33.3));
        DashboardStats.AccuracyBy(totals, t => t.Difficulty, t => t.DifficultyOrder).Should().Equal(
            new GroupAccuracy("Beginner", 75),
            new GroupAccuracy("Intermediate", 75),
            new GroupAccuracy("Advanced", 0));
    }

    private static DashboardAnswer Answer(string exercise, bool correct, DateTime at, int seconds = 10) =>
        new(exercise, correct, at, seconds);
}
