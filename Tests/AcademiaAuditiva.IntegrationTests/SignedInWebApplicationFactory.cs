using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// <see cref="TestWebApplicationFactory"/> where every request is authenticated as
/// <see cref="UserId"/> (a student), so tests can call <c>[Authorize]</c> endpoints
/// without the Identity register/confirm/login round trip. The user is not an
/// Identity cookie sign-in, so layouts still render the anonymous navigation.
/// </summary>
public class SignedInWebApplicationFactory : TestWebApplicationFactory
{
    public const string UserId = "integration-student";
    public const string UserName = "integration-student@example.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SignedInHandler>(SignedInHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = SignedInHandler.SchemeName);
        });
    }

    private sealed class SignedInHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "IntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, UserId),
                    new Claim(ClaimTypes.Name, UserName),
                    new Claim(ClaimTypes.Role, Models.RoleNames.Student),
                ],
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
