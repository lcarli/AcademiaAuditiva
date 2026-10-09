using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// Ready only when every audio source is in the image as it was measured, so an image without
/// them, or with other bytes, fails its readiness probe and the deploy. Once they are all there
/// that answer is kept.
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
                    $"{problems.Count} audio sources are not as measured in {library.Root}, such as {problems[0]}."));
            }

            _complete = true;
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
