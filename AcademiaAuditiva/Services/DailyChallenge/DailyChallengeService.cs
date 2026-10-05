using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.DailyChallenge;

public interface IDailyChallengeService
{
    /// <summary>Today's challenge in the player's time zone, with the player's progress on it.</summary>
    Task<DailyChallengeProgress> GetTodayAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default);
}

/// <summary>
/// Evaluates <see cref="DailyChallengeRules"/> against the player's answers, which come from
/// <see cref="PracticeHistory"/> like those of the learning path and the badges.
/// </summary>
public sealed class DailyChallengeService : IDailyChallengeService
{
    private readonly ApplicationDbContext _db;
    private readonly PracticeHistory _history;
    private readonly TimeProvider _clock;

    public DailyChallengeService(ApplicationDbContext db, PracticeHistory history, TimeProvider clock)
    {
        _db = db;
        _history = history;
        _clock = clock;
    }

    public async Task<DailyChallengeProgress> GetTodayAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default)
    {
        var today = PracticeStreak.LocalDate(_clock.GetUtcNow().UtcDateTime, timeZone);
        var exercises = await _db.Exercises.AsNoTracking()
            .Select(e => new ChallengeExercise(
                e.ExerciseId,
                e.Name,
                e.ExerciseCategory != null ? e.ExerciseCategory.Name : ""))
            .ToListAsync(ct);
        var answers = await _history.GetAsync(userId, ct);

        return DailyChallengeRules.Evaluate(today, exercises, answers, timeZone);
    }
}
