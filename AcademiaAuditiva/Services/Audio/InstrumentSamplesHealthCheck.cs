using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// Ready only when every bundled instrument has all its samples, so an image
/// without them fails its readiness probe, and the deploy, instead of the
/// exercises played on those instruments. The samples are part of the image,
/// so once they are all there that answer is kept.
/// </summary>
public sealed class InstrumentSamplesHealthCheck(BundledSamples samples) : IHealthCheck
{
    private volatile bool _complete;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_complete)
        {
            var missing = Instrument.Bundled.SelectMany(samples.Missing).ToList();
            if (missing.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{missing.Count} instrument samples are missing from {samples.Root}, such as {missing[0]}."));
            }

            _complete = true;
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
