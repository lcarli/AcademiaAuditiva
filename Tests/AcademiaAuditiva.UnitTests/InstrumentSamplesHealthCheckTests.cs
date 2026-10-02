using AcademiaAuditiva.Services.Audio;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The readiness probe keeps an image without the instrument samples from getting
/// traffic, instead of letting the exercises played on those instruments fail.
/// </summary>
public class InstrumentSamplesHealthCheckTests
{
    [Fact]
    public async Task IsHealthy_WhenEveryInstrumentHasAllItsNotes()
    {
        var check = new InstrumentSamplesHealthCheck(new BundledSamples(BundledSamplesTests.Root));

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task IsUnhealthy_WhenTheSamplesAreMissing()
    {
        var empty = Directory.CreateTempSubdirectory("aa-samples-");
        try
        {
            var check = new InstrumentSamplesHealthCheck(new BundledSamples(empty.FullName));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Description.Should().Be(
                $"168 instrument samples are missing from {empty.FullName}, such as guitar/C1.mp3.");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }
}
