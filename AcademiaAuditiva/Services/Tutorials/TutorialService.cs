using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.Tutorials;

public interface ITutorialService
{
    Task<bool> HasSeenAsync(string userId, string tutorialKey, CancellationToken ct = default);

    /// <summary>Records that the user closed the tour; <paramref name="finished"/> when they reached its last step.</summary>
    Task MarkSeenAsync(string userId, string tutorialKey, bool finished, CancellationToken ct = default);
}

public sealed class TutorialService : ITutorialService
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<TutorialService> _logger;

    public TutorialService(ApplicationDbContext db, TimeProvider clock, ILogger<TutorialService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public Task<bool> HasSeenAsync(string userId, string tutorialKey, CancellationToken ct = default)
        => _db.UserTutorials.AnyAsync(t => t.UserId == userId && t.TutorialKey == tutorialKey, ct);

    public async Task MarkSeenAsync(string userId, string tutorialKey, bool finished, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var row = await _db.UserTutorials.FirstOrDefaultAsync(t => t.UserId == userId && t.TutorialKey == tutorialKey, ct);
            if (row is null)
            {
                row = new UserTutorial
                {
                    UserId = userId,
                    TutorialKey = tutorialKey,
                    SeenAt = _clock.GetUtcNow().UtcDateTime,
                    Finished = finished
                };
                _db.UserTutorials.Add(row);
            }
            else if (finished && !row.Finished)
            {
                // A replay that reached the end; SeenAt keeps the first time.
                row.Finished = true;
            }
            else
            {
                return;
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 1)
            {
                // Usually another tab closed the same tour first and the unique
                // (UserId, TutorialKey) index rejected this row: the retry updates theirs.
                _db.Entry(row).State = EntityState.Detached;
                _logger.LogInformation(ex, "Saving the {Tutorial} tour conflicted; retrying.", tutorialKey);
            }
        }
    }
}
