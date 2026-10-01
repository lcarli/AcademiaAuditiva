using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The language menu posts to <c>/Dashboard/SetLanguage</c>, which stores the
/// culture cookie and sends the visitor back to the page they were on. Anyone
/// can call it, so it must require an antiforgery token, store only supported
/// cultures and only redirect within the site.
/// </summary>
public class LanguageSwitchTests : IClassFixture<TestWebApplicationFactory>
{
    private const string CultureCookie = ".AspNetCore.Culture";
    private readonly TestWebApplicationFactory _factory;

    public LanguageSwitchTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Any page with the layout renders the language form, which sets the antiforgery cookie.
    private static async Task<string> GetFormTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Home/Privacy");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the layout renders the language form");
        return match.Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string? token, string culture, string returnUrl)
    {
        var form = new Dictionary<string, string> { ["culture"] = culture, ["returnUrl"] = returnUrl };
        if (token is not null)
        {
            form["__RequestVerificationToken"] = token;
        }
        return client.PostAsync("/Dashboard/SetLanguage", new FormUrlEncodedContent(form));
    }

    private static string? CultureCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(CultureCookie + "=", StringComparison.Ordinal))
            : null;

    [Fact]
    public async Task SupportedCulture_IsStored_AndTheVisitorReturnsToTheSamePage()
    {
        var client = CreateClient();
        var token = await GetFormTokenAsync(client);

        var response = await PostAsync(client, token, "pt-br", "~/Exercise/GuessNote?keySelect=D");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Exercise/GuessNote?keySelect=D");
        var cookie = CultureCookieHeader(response);
        cookie.Should().StartWith(CultureCookie + "=c%3Dpt-BR%7Cuic%3Dpt-BR;", "the culture name is normalized");
        cookie!.ToLowerInvariant().Should().Contain("expires=").And.Contain("httponly").And.Contain("samesite=lax");
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("xx-XX")]
    [InlineData("not a culture")]
    [InlineData("")]
    public async Task UnsupportedCulture_IsIgnored(string culture)
    {
        var client = CreateClient();
        var token = await GetFormTokenAsync(client);

        var response = await PostAsync(client, token, culture, "~/Home/Privacy");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Home/Privacy");
        CultureCookieHeader(response).Should().BeNull();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("")]
    public async Task ReturnUrlOutsideTheSite_GoesHome(string returnUrl)
    {
        var client = CreateClient();
        var token = await GetFormTokenAsync(client);

        var response = await PostAsync(client, token, "fr-CA", returnUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task Post_WithoutAnAntiforgeryToken_IsRejected()
    {
        var response = await PostAsync(CreateClient(), token: null, "pt-BR", "/");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        CultureCookieHeader(response).Should().BeNull();
    }

    [Fact]
    public async Task Get_IsNotAllowed()
    {
        var response = await CreateClient().GetAsync("/Dashboard/SetLanguage?culture=pt-BR&returnUrl=/");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        CultureCookieHeader(response).Should().BeNull();
    }
}
