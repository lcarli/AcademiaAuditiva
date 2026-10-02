using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Explore mixes a clip for every sound it plays, so each learner may ask for 120 a
/// minute. The class has its own app, and so its own limiter.
/// </summary>
public class ExploreRateLimitTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public ExploreRateLimitTests(ExploreWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Play_AnswersTooManyRequests_OnceTheMinutesAllowanceIsUsed()
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        var note = new { kind = "note", root = "C", octave = 4 };

        for (var i = 1; i <= 120; i++)
        {
            var response = await client.PostAsJsonAsync("/Explore/Play", note);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "sound {0} is within the allowance", i);
        }

        (await client.PostAsJsonAsync("/Explore/Play", note)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _factory.Mixer.Plans.Should().HaveCount(120);
    }
}
