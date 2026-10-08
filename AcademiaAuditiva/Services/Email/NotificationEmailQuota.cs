using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Services.Email;

/// <summary>
/// Counts the notification e-mails of each UTC day in the database (<see cref="EmailDailyCount"/>),
/// so that every replica shares one <see cref="NotificationEmailOptions.DailyLimit"/>. An e-mail
/// counts once queued, whether or not it then goes out.
/// </summary>
public sealed class NotificationEmailQuota
{
    private const int Attempts = 5;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<NotificationEmailOptions> _options;
    private readonly ILogger<NotificationEmailQuota> _logger;

    public NotificationEmailQuota(IServiceScopeFactory scopes, TimeProvider clock,
        IOptionsMonitor<NotificationEmailOptions> options, ILogger<NotificationEmailQuota> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    /// <summary>Takes up to <paramref name="wanted"/> e-mails from today's allowance.</summary>
    /// <returns>How many it took: fewer than wanted once the day's limit is reached.</returns>
    public async Task<int> ReserveAsync(int wanted, CancellationToken cancellationToken = default)
    {
        var limit = _options.CurrentValue.DailyLimit;
        if (wanted <= 0 || limit <= 0) return 0;

        var day = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        for (var attempt = 1; ; attempt++)
        {
            // A context of its own each time: after a conflict the last one holds a stale count.
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var count = await db.EmailDailyCounts.FirstOrDefaultAsync(c => c.Day == day, cancellationToken);
            if (count is null)
            {
                count = new EmailDailyCount { Day = day };
                db.EmailDailyCounts.Add(count);
            }

            var granted = Math.Clamp(limit - count.Sent, 0, wanted);
            if (granted == 0) return 0;

            count.Sent += granted;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return granted;
            }
            // Another replica counted in between: the count changed (a concurrency token), or it
            // added the day's row first (a duplicate key).
            catch (DbUpdateException ex) when (attempt < Attempts)
            {
                _logger.LogInformation(ex, "The e-mail count of {Day} changed meanwhile; counting again", day);
            }
        }
    }
}
