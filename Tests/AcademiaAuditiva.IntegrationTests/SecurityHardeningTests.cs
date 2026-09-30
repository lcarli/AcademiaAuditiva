using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Regression guards for the security hotfixes: endpoints that trusted a client-supplied
/// score or user id must stay unroutable, the app must honour the ingress'
/// <c>X-Forwarded-Proto</c> so absolute URLs it generates (login redirects, email links,
/// OAuth callbacks) keep the https scheme behind Azure Container Apps, and Identity pages
/// must load the patched jquery-validation.
/// </summary>
public class SecurityHardeningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SecurityHardeningTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Both controllers are [Authorize], so a surviving action would answer 302 to the login page.
    [Theory]
    [InlineData("/Exercise/GuessIntervalSaveScore")]
    [InlineData("/Exercise/GuessQualitySaveScore")]
    [InlineData("/Exercise/GuessFunctionSaveScore")]
    [InlineData("/Exercise/GuessFullIntervalSaveScore")]
    [InlineData("/Exercise/GuessMissingNoteSaveScore")]
    [InlineData("/Dashboard/GetBestScoresForUser?userId=someone-else")]
    public async Task RemovedEndpoints_AreNotRoutable(string path)
    {
        var response = await CreateClient().PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["correctCount"] = "999",
            ["errorCount"] = "0",
            ["timeSpentSeconds"] = "1"
        }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, "http")]
    [InlineData("https", "https")]
    public async Task ForwardedProto_DrivesSchemeOfGeneratedUrls(string? forwardedProto, string expectedScheme)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Dashboard");
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-For", "203.0.113.10");
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        var response = await CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.Scheme.Should().Be(expectedScheme);
    }

    // Guards the Areas/Identity override; without it the Identity UI package's partial
    // would load its own older jquery-validation copy.
    [Fact]
    public async Task IdentityPages_UseTheAppsPatchedValidationScripts()
    {
        var html = await CreateClient().GetStringAsync("/Identity/Account/Login");

        html.Should().Contain("/lib/jquery-validation/dist/jquery.validate.min.js?v=")
            .And.NotContain("/Identity/lib/jquery-validation")
            .And.NotContain("ajax/libs/jquery-validate/");
    }
}
