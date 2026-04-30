using System.Net;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Smoke tests that prove the full ASP.NET Core pipeline (routing,
/// authentication, authorization, localization, Identity UI) boots end
/// to end against an in-memory EF store. These guard the wiring that
/// every other integration scenario depends on, without simulating a
/// full register-confirm-practice journey (Identity confirmation tokens
/// require email round-trips out of scope for unit-runner CI).
/// </summary>
public class PipelineSmokeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PipelineSmokeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HomePage_RespondsOk_ForAnonymousUser()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoginPage_RespondsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Identity/Account/Login");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegisterPage_RespondsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Identity/Account/Register");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedExerciseAction_RedirectsToLogin_WhenAnonymous()
    {
        // ExerciseController has [Authorize] at class level. An anonymous
        // POST to ValidateExercise must NOT be served — the cookie auth
        // handler should either 302 to the login page or 401 outright.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsync("/Exercise/ValidateExercise", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized);
    }
}

