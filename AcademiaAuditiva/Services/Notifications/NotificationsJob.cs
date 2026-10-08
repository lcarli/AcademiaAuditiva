namespace AcademiaAuditiva.Services.Notifications;

/// <summary>
/// Once the app has started, then every hour: deletes the notifications past their retention and
/// reminds students of the routines due the next day (<see cref="Notifier"/>). Every replica runs
/// it; each student is still reminded once.
/// </summary>
public sealed class NotificationsJob : BackgroundService
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationsJob> _logger;

    public NotificationsJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NotificationsJob> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, _clock);
        try
        {
            do
            {
                await RunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Logs what fails rather than throw: an exception out of ExecuteAsync would stop the app.
    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        try
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<Notifier>().PurgeAsync(stoppingToken);
            if (deleted > 0) _logger.LogInformation("Deleted {Count} old notifications", deleted);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not delete the old notifications");
        }

        try
        {
            var reminded = await scope.ServiceProvider.GetRequiredService<Notifier>().RemindDueTomorrowAsync(stoppingToken);
            if (reminded > 0) _logger.LogInformation("Reminded {Count} students of the routines due tomorrow", reminded);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not remind the students of the routines due tomorrow");
        }
    }
}
