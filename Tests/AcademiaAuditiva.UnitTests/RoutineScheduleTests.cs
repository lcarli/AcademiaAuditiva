using System.Globalization;
using AcademiaAuditiva.Services.Routines;

namespace AcademiaAuditiva.UnitTests;

/// <summary>When a routine stops taking answers: <see cref="RoutineSchedule"/>.</summary>
public class RoutineScheduleTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static readonly DateTime DueDate = new(2026, 1, 14);

    [Fact]
    public void WithoutADueDate_ARoutineStaysOpen()
    {
        RoutineSchedule.Window(null, allowLate: false, Utc("2099-01-01T00:00:00Z"), TimeZoneInfo.Utc)
            .Should().Be(RoutineWindow.Open);
    }

    [Theory]
    // The last second of the due date in Toronto (UTC-5 in January), then its midnight.
    [InlineData("2026-01-15T04:59:59Z", RoutineWindow.Open)]
    [InlineData("2026-01-15T05:00:00Z", RoutineWindow.Closed)]
    public void ARoutine_IsDueByTheEndOfItsDueDate_InTheStudentsTimeZone(string now, RoutineWindow expected)
    {
        RoutineSchedule.Window(DueDate, allowLate: false, Utc(now), Toronto).Should().Be(expected);
    }

    [Fact]
    public void TheSameInstant_CanBeOnTheDueDateForOneStudent_AndPastItForAnother()
    {
        var now = Utc("2026-01-14T16:00:00Z"); // 11:00 in Toronto, 01:00 the next day in Tokyo

        RoutineSchedule.Window(DueDate, allowLate: false, now, Toronto).Should().Be(RoutineWindow.Open);
        RoutineSchedule.Window(DueDate, allowLate: false, now, TimeZoneInfo.Utc).Should().Be(RoutineWindow.Open);
        RoutineSchedule.Window(DueDate, allowLate: false, now, Tokyo).Should().Be(RoutineWindow.Closed);
    }

    [Theory]
    [InlineData(false, RoutineWindow.Closed)]
    [InlineData(true, RoutineWindow.Late)]
    public void PastTheDueDate_ARoutineIsLate_OnlyWhenTheTeacherAcceptsLateAnswers(bool allowLate, RoutineWindow expected)
    {
        RoutineSchedule.Window(DueDate, allowLate, Utc("2026-03-01T12:00:00Z"), TimeZoneInfo.Utc).Should().Be(expected);
    }

    [Fact]
    public void OnTheDueDate_ARoutineThatAcceptsLateAnswers_IsStillOpen()
    {
        RoutineSchedule.Window(DueDate, allowLate: true, Utc("2026-01-14T23:59:59Z"), TimeZoneInfo.Utc)
            .Should().Be(RoutineWindow.Open);
    }

    [Fact]
    public void OnlyTheDayOfTheDueDate_Counts_NotItsTime()
    {
        RoutineSchedule.Window(DueDate.AddHours(9), allowLate: false, Utc("2026-01-14T20:00:00Z"), TimeZoneInfo.Utc)
            .Should().Be(RoutineWindow.Open);
    }

    private static DateTime Utc(string instant)
        => DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).UtcDateTime;
}
