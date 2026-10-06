using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>Whether a routine still takes answers.</summary>
public enum RoutineWindow
{
    /// <summary>No due date, or the due date has not passed yet.</summary>
    Open,

    /// <summary>The due date has passed, but the teacher still accepts answers.</summary>
    Late,

    /// <summary>The due date has passed and the routine takes no more answers.</summary>
    Closed
}

public static class RoutineSchedule
{
    /// <summary>
    /// A routine is due by the end of its due date in the student's time zone
    /// (<see cref="UserTimeZone"/>); after that it is late when the teacher allows
    /// late answers, and closed otherwise. Without a due date it stays open.
    /// </summary>
    public static RoutineWindow Window(DateTime? dueAt, bool allowLate, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        if (dueAt is not { } due || PracticeStreak.LocalDate(nowUtc, timeZone) <= DateOnly.FromDateTime(due))
        {
            return RoutineWindow.Open;
        }
        return allowLate ? RoutineWindow.Late : RoutineWindow.Closed;
    }
}
