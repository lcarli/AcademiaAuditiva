using AcademiaAuditiva.Interfaces;
using Azure.Storage.Blobs;
using System.Text.Json;

public class AnalyticsService : IAnalyticsService
{
    private readonly BlobContainerClient _containerClient;

    public AnalyticsService(IConfiguration config)
    {
        var connectionString = config["st-account"];
        var containerName = "exercise-logs";
        var serviceClient = new BlobServiceClient(connectionString);
        _containerClient = serviceClient.GetBlobContainerClient(containerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task SaveAttemptAsync(ExerciseAttemptLog log)
    {
        var blobName = $"{log.UserId}/{log.Exercise}/{log.Timestamp:yyyyMMdd-HHmmssfff}.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blobClient.UploadAsync(stream, overwrite: true);
    }

    public async Task<List<ExerciseAttemptLog>> GetAttemptsAsync(string userId, string? exercise = null)
    {
        var prefix = exercise == null ? userId : $"{userId}/{exercise}";
        var logs = new List<ExerciseAttemptLog>();

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