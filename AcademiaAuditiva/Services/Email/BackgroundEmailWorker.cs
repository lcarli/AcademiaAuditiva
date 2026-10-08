namespace AcademiaAuditiva.Services.Email;

/// <summary>Runs the jobs of <see cref="BackgroundEmailQueue"/>; one that fails is logged and skipped.</summary>
public sealed class BackgroundEmailWorker : BackgroundService
{
    private readonly BackgroundEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackgroundEmailWorker> _logger;

    public BackgroundEmailWorker(BackgroundEmailQueue queue, IServiceScopeFactory scopes, ILogger<BackgroundEmailWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await job(scope.ServiceProvider, stoppingToken);
                }
                // Left pending, for StopAsync to count.
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An e-mail job failed");
                }

                _queue.Finished();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_queue.Pending > 0)
        {
            _logger.LogWarning("The app stopped before {Count} e-mail jobs were done", _queue.Pending);
        }
    }
}
