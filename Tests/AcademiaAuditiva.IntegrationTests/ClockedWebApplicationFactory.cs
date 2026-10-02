using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// <see cref="TestWebApplicationFactory"/> whose sign-in cookie checks (the security
/// stamp validator) read <see cref="Clock"/>, so a test can move past the validation
/// interval without waiting for it.
/// </summary>
public class ClockedWebApplicationFactory : TestWebApplicationFactory
{
    public OffsetClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock));
    }

    /// <summary>The system time plus <see cref="Offset"/>.</summary>
    public sealed class OffsetClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }
}
