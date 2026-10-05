using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.Gamification;

/// <summary>
/// The player's graded answers, read once per request (scoped) and shared by the services that
/// evaluate them: XP and badges, and the learning path. Saving a <see cref="ScoreSnapshot"/> through
/// the same context discards the copy, so the next read includes it (ExecuteDelete and raw SQL don't).
/// </summary>
public sealed class PracticeHistory
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<string, IReadOnlyList<PracticeAnswer>> _byUser = new(StringComparer.Ordinal);

    public PracticeHistory(ApplicationDbContext db)
    {
        _db = db;
        _db.SavingChanges += (_, _) =>
        {
            if (_byUser.Count > 0 && _db.ChangeTracker.Entries<ScoreSnapshot>()
                    .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                _byUser.Clear();
            }
        };
    }

    /// <summary>Every answer of the player, oldest first, with UTC timestamps.</summary>
    public async Task<IReadOnlyList<PracticeAnswer>> GetAsync(string userId, CancellationToken ct = default)
    {
        if (_byUser.TryGetValue(userId, out var cached)) return cached;

        var answers = await _db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp).ThenBy(s => s.Id)
            .Select(s => new PracticeAnswer(s.ExerciseId, s.IsCorrect, s.Timestamp, s.FilterJson))
            .ToListAsync(ct);
        for (var i = 0; i < answers.Count; i++)
        {
            // SQL Server datetime2 comes back as DateTimeKind.Unspecified; the app always stores UTC.
            answers[i] = answers[i] with { Timestamp = DateTime.SpecifyKind(answers[i].Timestamp, DateTimeKind.Utc) };
        }

        var history = answers.AsReadOnly();
        _byUser[userId] = history;
        return history;
    }
}
