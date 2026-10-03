using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The sign-in, two-factor, registration and e-mail forms share one allowance per
/// client address: <see cref="Limit"/> POSTs a minute here, 30 in production. The
/// forms go out empty and without an antiforgery token, so the allowed ones stop at
/// the antiforgery check (400) and change nothing; the limiter runs before it. Each
/// test sends from its own address through X-Forwarded-For, as the ingress does, and
/// the class has its own app, so nothing else uses up the allowance.
/// </summary>
public class AccountFormsRateLimitTests : IClassFixture<AccountFormsRateLimitTests.Factory>
{
    private const int Limit = 3;

    private static int _lastAddress = 10;

    private readonly Factory _factory;

    public AccountFormsRateLimitTests(Factory factory) => _factory = factory;

    public static TheoryData<string> Forms => new()
    {
        "/Identity/Account/Login",
        "/Identity/Account/LoginWith2fa",
        "/Identity/Account/LoginWithRecoveryCode",
        "/Identity/Account/Register",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ResendEmailConfirmation",
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task EachForm_AnswersTooManyRequests_OnceTheAllowanceIsUsed(string form)
    {
        var client = NewClient();
        var address = NextAddress();
        for (var i = 1; i <= Limit; i++)
        {
            (await SendAsync(client, HttpMethod.Post, form, address)).StatusCode
                .Should().Be(HttpStatusCode.BadRequest, "post {0} is within the allowance", i);
        }

        var response = await SendAsync(client, HttpMethod.Post, form, address);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await response.Content.ReadAsStringAsync()).Should().Contain("<h1>Too many attempts</h1>");
    }

    [Fact]
    public async Task TheFormsShareTheAllowance_AndOnlyTheirPostsCount()
    {
        var client = NewClient();
        var address = NextAddress();
        foreach (var form in new[] { "/Identity/Account/Login", "/Identity/Account/Register", "/Identity/Account/ForgotPassword" })
        {
            (await SendAsync(client, HttpMethod.Post, form, address)).StatusCode.Should().Be(HttpStatusCode.BadRequest, form);
        }

        (await SendAsync(client, HttpMethod.Post, "/Identity/Account/LoginWith2fa", address)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, HttpMethod.Get, "/Identity/Account/Login", address)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the pages still load");
        (await SendAsync(client, HttpMethod.Post, "/Identity/Account/ResetPassword", address)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the reset form needs the e-mailed code, so it isn't limited");
        (await SendAsync(client, HttpMethod.Post, "/Identity/Account/Login", NextAddress())).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "another address has its own allowance");
    }

    [Fact]
    public async Task TheRefusal_IsAPageInTheVisitorsLanguage_ThatLinksBackToTheForm()
    {
        // The sign-in form a visitor reaches from an exercise.
        const string form = "/Identity/Account/Login?ReturnUrl=%2FExercise%2FGuessNote%3FkeySelect%3DD";
        var client = NewClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR");
        var address = NextAddress();
        for (var i = 1; i <= Limit; i++)
        {
            await SendAsync(client, HttpMethod.Post, form, address);
        }

        var response = await SendAsync(client, HttpMethod.Post, form, address);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Should()
            .Contain("<html lang=\"pt\">")
            .And.Contain("<h1>Muitas tentativas</h1>")
            .And.Contain("<p>Houve tentativas demais a partir da sua rede. Aguarde um minuto e tente novamente.</p>")
            .And.Contain($"<a href=\"{form}\">Voltar</a>");
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // A new documentation address (TEST-NET-3) on every call.
    private static string NextAddress() => $"203.0.113.{Interlocked.Increment(ref _lastAddress)}";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string address)
    {
        using var request = new HttpRequestMessage(method, url);
        if (method == HttpMethod.Post)
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());
        }
        request.Headers.Add("X-Forwarded-For", address);
        return await client.SendAsync(request);
    }

    public sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Added after the base factory's settings, so it wins.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AccountForms:PermitLimit"] = Limit.ToString(CultureInfo.InvariantCulture),
            }));
        }
    }
}
