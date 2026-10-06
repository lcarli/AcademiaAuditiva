using System.Globalization;
using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Areas.Teacher.Services;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The figures of the teacher's reports (#107): <see cref="RoutineReportRules"/> and the report records.</summary>
public class RoutineReportRulesTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly DateTime DueDate = new(2026, 1, 14);
    private static readonly ReportStudent Student = new("student", "ana", "ana@example.test");

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 4, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(1, 8, 13)] // 12.5 rounds up
    [InlineData(5, 5, 100)]
    public void Percent_IsRounded_AndEmptyBeforeTheFirstAnswer(int correct, int attempts, int? expected)
    {
        RoutineReportRules.Percent(correct, attempts).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 4, 0)]
    [InlineData(5, 2, 3)] // 2.5 rounds up
    [InlineData(7, 3, 2)]
    [InlineData(300, 1, 300)]
    public void PerAnswer_IsRounded_AndEmptyBeforeTheFirstAnswer(long seconds, int attempts, int? expected)
    {
        RoutineReportRules.PerAnswer(seconds, attempts).Should().Be(expected);
    }

    [Fact]
    public void Status_IsNotStarted_BeforeTheFirstAnswer()
    {
        RoutineReportRules.Status([Progress(0, target: 5), Progress(0, target: 3)]).Should().Be(TakeStatus.NotStarted);
        RoutineReportRules.Status([]).Should().Be(TakeStatus.NotStarted);
    }

    [Fact]
    public void Status_IsInProgress_FromTheFirstAnswer_UntilEveryItemIsComplete()
    {
        RoutineReportRules.Status([Progress(1, target: 5), Progress(0, target: 3)]).Should().Be(TakeStatus.InProgress);
        RoutineReportRules.Status([Progress(5, target: 5), Progress(0, target: 3)]).Should().Be(TakeStatus.InProgress);
        RoutineReportRules.Status([Progress(5, target: 5), Progress(2, target: 3)]).Should().Be(TakeStatus.InProgress);
    }

    [Fact]
    public void Status_IsFinished_OnceEveryItemIsComplete_RightOrWrong()
    {
        RoutineReportRules.Status([Progress(5, target: 5, correct: 0), Progress(3, target: 3, correct: 3)])
            .Should().Be(TakeStatus.Finished);
    }

    [Theory]
    [InlineData(TakeStatus.Finished)]
    [InlineData(TakeStatus.InProgress)]
    [InlineData(TakeStatus.NotStarted)]
    public void WithoutADueDate_NoTakeIsLate(TakeStatus status)
    {
        var finished = status == TakeStatus.Finished ? Utc("2099-01-01T00:00:00Z") : (DateTime?)null;

        RoutineReportRules.IsLate(status, finished, dueAt: null, allowLate: true, Utc("2099-01-02T00:00:00Z"), TimeZoneInfo.Utc)
            .Should().BeFalse();
    }

    [Theory]
    // The last second of the due date in Toronto (UTC-5 in January), then its midnight.
    [InlineData("2026-01-15T04:59:59Z", false)]
    [InlineData("2026-01-15T05:00:00Z", true)]
    public void AFinishedTake_IsLate_WhenItsLastAnswerCameAfterTheDueDate(string finished, bool expected)
    {
        RoutineReportRules.IsLate(TakeStatus.Finished, Utc(finished), DueDate, allowLate: true, Utc("2026-03-01T00:00:00Z"), Toronto)
            .Should().Be(expected);
    }

    [Fact]
    public void AFinishedTake_IsNotLate_WhenTheRoutineTookNoLateAnswers()
    {
        // The student's own time zone let them answer, though it is past the due date in the teacher's.
        RoutineReportRules.IsLate(TakeStatus.Finished, Utc("2026-01-15T06:00:00Z"), DueDate, allowLate: false, Utc("2026-03-01T00:00:00Z"), Toronto)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(TakeStatus.InProgress, false)]
    [InlineData(TakeStatus.InProgress, true)]
    [InlineData(TakeStatus.NotStarted, false)]
    [InlineData(TakeStatus.NotStarted, true)]
    public void AnUnfinishedTake_IsLate_OnceTheDueDateHasPassed(TakeStatus status, bool allowLate)
    {
        RoutineReportRules.IsLate(status, null, DueDate, allowLate, Utc("2026-01-15T04:59:59Z"), Toronto)
            .Should().BeFalse("it is still the due date in Toronto");
        RoutineReportRules.IsLate(status, null, DueDate, allowLate, Utc("2026-01-15T05:00:00Z"), Toronto)
            .Should().BeTrue("the due date has passed in Toronto");
    }

    [Fact]
    public void ATake_GivesItsTimes_InTheTeachersTimeZone()
    {
        var take = RoutineReportRules.Take(
            Student,
            [Item(5, target: 5, correct: 4, seconds: 20), null, Item(3, target: 3, correct: 3, seconds: 9)],
            lastAnswerUtc: Utc("2026-01-15T04:30:00Z"),
            DueDate,
            allowLate: true,
            nowUtc: Utc("2026-01-20T00:00:00Z"),
            Toronto);

        take.Status.Should().Be(TakeStatus.Finished);
        take.IsLate.Should().BeFalse("its last answer came at 23:30 on the due date in Toronto");
        take.FinishedAt.Should().Be(new DateTime(2026, 1, 14, 23, 30, 0));
        take.LastAnswerAt.Should().Be(new DateTime(2026, 1, 14, 23, 30, 0));
    }

    [Fact]
    public void AnUnfinishedTake_HasNoFinishTime_ButKeepsItsLastAnswer()
    {
        var take = RoutineReportRules.Take(
            Student,
            [Item(2, target: 5, correct: 1, seconds: 8)],
            lastAnswerUtc: Utc("2026-01-10T12:00:00Z"),
            DueDate,
            allowLate: false,
            nowUtc: Utc("2026-01-20T00:00:00Z"),
            TimeZoneInfo.Utc);

        take.Status.Should().Be(TakeStatus.InProgress);
        take.IsLate.Should().BeTrue();
        take.FinishedAt.Should().BeNull();
        take.LastAnswerAt.Should().Be(new DateTime(2026, 1, 10, 12, 0, 0));
    }

    [Fact]
    public void ATake_CountsOnlyTheItemsTheStudentHas_AndAnswersBeyondTheTargetOnlyInTheAccuracy()
    {
        var take = RoutineReportRules.Take(
            Student,
            [Item(6, target: 5, correct: 3, seconds: 30), null, Item(1, target: 4, correct: 1, seconds: 5)],
            lastAnswerUtc: Utc("2026-01-10T12:00:00Z"),
            dueAt: null,
            allowLate: false,
            nowUtc: Utc("2026-01-20T00:00:00Z"),
            TimeZoneInfo.Utc);

        take.Questions.Should().Be(9);
        take.Answered.Should().Be(6, "5 of the first item's answers count, and the second item is excluded");
        take.Attempts.Should().Be(7);
        take.Correct.Should().Be(4);
        take.Accuracy.Should().Be(57);
        take.SecondsPerAnswer.Should().Be(5);
    }

    [Fact]
    public void Totals_CountTheTakes_AndWeighEachAnswerTheSame()
    {
        var finished = Take(TakeStatus.Finished, isLate: true, Item(10, target: 10, correct: 9));
        var started = Take(TakeStatus.InProgress, isLate: false, Item(2, target: 10, correct: 0));
        var waiting = Take(TakeStatus.NotStarted, isLate: true, Item(0, target: 10, correct: 0));

        var totals = TakeTotals.Of([finished, started, waiting]);

        totals.Should().Be(new TakeTotals(Takes: 3, Finished: 1, InProgress: 1, NotStarted: 1, Late: 2, Attempts: 12, Correct: 9));
        totals.Accuracy.Should().Be(75, "9 of 12 answers are right, not the average of 90% and 0%");
        TakeTotals.Of([]).Accuracy.Should().BeNull();
    }

    [Fact]
    public void Totals_AddUp()
    {
        var sum = TakeTotals.Sum(
        [
            new TakeTotals(2, 1, 1, 0, 1, 10, 7),
            new TakeTotals(3, 0, 1, 2, 2, 4, 1),
        ]);

        sum.Should().Be(new TakeTotals(5, 1, 2, 2, 3, 14, 8));
        sum.Accuracy.Should().Be(57);
    }

    [Theory]
    [InlineData("ana@example.test", "ana@example.test", false)]
    [InlineData("Ana@Example.test", "ana@example.test", false)]
    [InlineData("ana", "ana@example.test", true)]
    [InlineData("ana", "", false)]
    public void Students_ShowTheirEmail_OnlyWhenItIsNotTheirUserName(string userName, string email, bool shown)
    {
        new ReportStudent("student", userName, email).ShowsEmail.Should().Be(shown);
    }

    private static RoutineItemProgress Progress(int attempts, int target, int? correct = null)
        => new(attempts, correct ?? attempts, target, MinScore: null);

    private static TakeItem Item(int attempts, int target, int correct, long seconds = 0)
        => new(new RoutineItemProgress(attempts, correct, target, MinScore: null), seconds);

    private static RoutineTake Take(TakeStatus status, bool isLate, params TakeItem?[] items)
        => new(Student, items, status, isLate, FinishedAt: null, LastAnswerAt: null);

    private static DateTime Utc(string instant)
        => DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).UtcDateTime;
}
