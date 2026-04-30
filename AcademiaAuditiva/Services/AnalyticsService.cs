using AcademiaAuditiva.Interfaces;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class AnalyticsService : IAnalyticsService
{
    private readonly BlobContainerClient? _containerClient;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(IConfiguration config, ILogger<AnalyticsService> logger)
    {
        _logger = logger;
        // The Storage account connection string is optional. When absent — e.g.
        // local dev without storage emulator, or a deployment that hasn't yet
        // provisioned the analytics blob — the service degrades to a silent
        // no-op so that the controller hot path never fails to activate.
        var connectionString = config["st-account"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogInformation(
                "AnalyticsService running in no-op mode: no 'st-account' connection string configured.");
            return;
        }

        try
        {
            var serviceClient = new BlobServiceClient(connectionString);
            _containerClient = serviceClient.GetBlobContainerClient("exercise-logs");
            _containerClient.CreateIfNotExists();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AnalyticsService failed to initialize blob container; falling back to no-op mode.");
            _containerClient = null;
        }
    }

    public async Task SaveAttemptAsync(ExerciseAttemptLog log)
    {
        if (_containerClient is null)
        {
            return;
        }

        var blobName = $"{log.UserId}/{log.Exercise}/{log.Timestamp:yyyyMMdd-HHmmssfff}.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        try
        {
            await blobClient.UploadAsync(stream, overwrite: true);
        }
        catch (Exception ex)
        {
            // Analytics is best-effort: never fail an exercise submission
            // because the blob write was rejected.
            _logger.LogWarning(ex, "AnalyticsService failed to persist attempt for user {UserId}.", log.UserId);
        }
    }

    public async Task<List<ExerciseAttemptLog>> GetAttemptsAsync(string userId, string? exercise = null)
    {
        var logs = new List<ExerciseAttemptLog>();
        if (_containerClient is null)
        {
            return logs;
        }

        var prefix = exercise == null ? userId : $"{userId}/{exercise}";

        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix))
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            var downloadInfo = await blobClient.DownloadAsync();
            using var streamReader = new StreamReader(downloadInfo.Value.Content);
            var json = await streamReader.ReadToEndAsync();
            var log = JsonSerializer.Deserialize<ExerciseAttemptLog>(json);
            if (log != null)
            {
                logs.Add(log);
            }
        }

        return logs;
    }
}