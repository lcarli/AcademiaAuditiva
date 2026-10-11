using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// Ready only when every bundled source is in the image as measured and the configured private
/// storage catalog contains all prepared chunks. Once both catalogs are complete that answer is kept.
/// </summary>
public sealed class AudioSourcesHealthCheck(AudioSourceLibrary library) : IHealthCheck
{
    private volatile bool _complete;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_complete)
        {
            var problems = library.Problems();
            if (problems.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{problems.Count} audio source problems were found, such as {problems[0]}."));
            }

            _complete = true;
        }

        return Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object>
        {
            ["privateStorage"] = library.UsesPrivateStorage,
            ["preparedChunks"] = library.Sources.Count(source => source.IsRemote),
        }));
    }
}
