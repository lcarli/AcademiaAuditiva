namespace AcademiaAuditiva.Services.Gamification;

public static class Leveling
{
    public const int XpPerCorrectAnswer = 10;
    public const int XpPerWrongAnswer = 2;
    public const int XpPerBadge = 50;
    public const int MaxLevel = 999;

    /// <summary>Ranks are dynamics markings, from pianissimo to fortississimo.</summary>
    public static IReadOnlyList<string> Ranks { get; } = ["pp", "p", "mp", "mf", "f", "ff", "fff"];

    /// <summary>Total XP needed to reach <paramref name="level"/>: 0, 100, 300, 600, 1000…</summary>
    public static int XpForLevel(int level)
    {
        if (level <= 1) return 0;
        var xp = 50L * level * (level - 1);
        return xp > int.MaxValue ? int.MaxValue : (int)xp;
    }

    public static int LevelForXp(int xp)
    {
        var level = 1;
        while (level < MaxLevel && XpForLevel(level + 1) <= xp) level++;
        return level;
    }

    public static string RankForLevel(int level) => level switch
    {
        <= 2 => "pp",
        <= 4 => "p",
        <= 6 => "mp",
        <= 9 => "mf",
        <= 14 => "f",
        <= 19 => "ff",
        _ => "fff"
    };
}

/// <param name="Current">Consecutive local days with practice, ending today or yesterday.</param>
/// <param name="PracticedToday">False while the current streak still waits for today's practice.</param>
public readonly record struct StreakInfo(int Current, int Best, bool PracticedToday);

public static class PracticeStreak
{
    public static StreakInfo Compute(IEnumerable<DateTime> utcTimestamps, TimeZoneInfo timeZone, DateTime nowUtc)
    {
        var days = new SortedSet<DateOnly>(utcTimestamps.Select(t => LocalDate(t, timeZone)));
        if (days.Count == 0) return default;

        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days)
        {
            run = previous is { } p && day.DayNumber == p.DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }

        var today = LocalDate(nowUtc, timeZone);
        var practicedToday = days.Contains(today);
        var current = 0;
        for (var day = practicedToday ? today : today.AddDays(-1); days.Contains(day); day = day.AddDays(-1))
        {
            current++;
        }

        return new StreakInfo(current, best, practicedToday);
    }

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo timeZone) => DateOnly.FromDateTime(LocalTime(utc, timeZone));

    public static DateTime LocalTime(DateTime utc, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
}

/// <summary>
/// Level, XP and streak, all derived from the player's answers and badges:
/// nothing here is stored, so the rules can change without a migration.
/// </summary>
public sealed record PlayerProgress(int Xp, int Level, string Rank, int CorrectAnswers, int TotalAnswers, StreakInfo Streak)
{
    public int LevelStartXp => Leveling.XpForLevel(Level);

    public int NextLevelXp => Leveling.XpForLevel(Level + 1);

    public int XpToNextLevel => Math.Max(0, NextLevelXp - Xp);

    public int LevelPercent => NextLevelXp <= LevelStartXp
        ? 100
        : (int)Math.Clamp(100L * (Xp - LevelStartXp) / (NextLevelXp - LevelStartXp), 0, 100);

    public static PlayerProgress From(int correctAnswers, int totalAnswers, int badges, StreakInfo streak)
    {
        var xp = correctAnswers * Leveling.XpPerCorrectAnswer
            + (totalAnswers - correctAnswers) * Leveling.XpPerWrongAnswer
            + badges * Leveling.XpPerBadge;
        var level = Leveling.LevelForXp(xp);
        return new PlayerProgress(xp, level, Leveling.RankForLevel(level), correctAnswers, totalAnswers, streak);
    }
}
