using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A labelled, unvaried source sample is a reference to match round clips
/// against, so only admins (<c>scripts/local-audio.ps1</c>) may download them.
/// </summary>
public class AudioAccessTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;

    public AudioAccessTests(SignedInWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // [ApiController] endpoints answer 403 instead of redirecting to the access-denied page.
    [Fact]
    public async Task Students_CannotDownloadSourceSamples()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/audio/C4.mp3");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
