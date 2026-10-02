using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Creating the authenticator key (opening the set-up page) and turning two-factor
/// sign-in on or off change the account's security stamp, which ends every session
/// at its next cookie check, a minute at most (Program.cs). The session that made
/// the change must go on with a cookie carrying the new stamp, while the account's
/// other sessions end. These sign in for real and move past the check interval.
/// </summary>
public class TwoFactorSettingsSessionTests : IClassFixture<ClockedWebApplicationFactory>
{
    private const string Password = "Two-Factor-Session!Pass1";

    // Any page that needs a signed-in user.
    private const string SignedInPage = "/Identity/Account/Manage";
    private const string SetupPage = "/Identity/Account/Manage/EnableAuthenticator";
    private const string DisablePage = "/Identity/Account/Manage/Disable2fa";

    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly ClockedWebApplicationFactory _factory;

    public TwoFactorSettingsSessionTests(ClockedWebApplicationFactory factory) => _factory = factory;

    private enum Setup { NoAuthenticator, AuthenticatorNotVerified, TwoFactorOn }

    private sealed record TestUser(string Id, string Email, string? AuthenticatorKey, bool TwoFactorOn);

    [Fact]
    public async Task OpeningTheAuthenticatorSetup_KeepsThisSessionSignedIn()
    {
        var user = await CreateUserAsync(Setup.NoAuthenticator);
        var client = await SignedInClientAsync(user);
        var otherClient = await SignedInClientAsync(user);
        var stamp = (await FindAsync(user.Id)).SecurityStamp;

        var page = await PageAsync(client, SetupPage);

        var key = await AuthenticatorKeyAsync(user.Id);
        key.Should().NotBeNullOrEmpty("opening the page creates the key");
        page.Should().Contain($"secret={key}", "the page shows the new key");
        (await FindAsync(user.Id)).SecurityStamp.Should().NotBe(stamp);
        await ExpectAfterTheNextCookieCheckAsync(signedIn: client, signedOut: otherClient);
    }

    [Fact]
    public async Task TurningTwoFactorOn_KeepsThisSessionSignedIn()
    {
        // The key exists already, so only turning two-factor on changes the stamp.
        var user = await CreateUserAsync(Setup.AuthenticatorNotVerified);
        var client = await SignedInClientAsync(user);
        var otherClient = await SignedInClientAsync(user);
        var stamp = (await FindAsync(user.Id)).SecurityStamp;
        var page = await PageAsync(client, SetupPage);
        (await FindAsync(user.Id)).SecurityStamp.Should().Be(stamp, "the page shows the existing key");

        var response = await client.PostAsync(SetupPage, Form(page, ("Input.Code", AuthenticatorApp.Code(user.AuthenticatorKey!))));

        ExpectRedirect(response, "/Identity/Account/Manage/ShowRecoveryCodes");
        var saved = await FindAsync(user.Id);
        saved.TwoFactorEnabled.Should().BeTrue();
        saved.SecurityStamp.Should().NotBe(stamp);
        var codes = await PageAsync(client, "/Identity/Account/Manage/ShowRecoveryCodes");
        Regex.Matches(codes, "<code class=\"recovery-code\">").Should().HaveCount(10, "the new recovery codes are shown");
        await ExpectAfterTheNextCookieCheckAsync(signedIn: client, signedOut: otherClient);
    }

    [Fact]
    public async Task TurningTwoFactorOff_KeepsThisSessionSignedIn()
    {
        var user = await CreateUserAsync(Setup.TwoFactorOn);
        var client = await SignedInClientAsync(user);
        var otherClient = await SignedInClientAsync(user);
        var stamp = (await FindAsync(user.Id)).SecurityStamp;
        var page = await PageAsync(client, DisablePage);

        var response = await client.PostAsync(DisablePage, Form(page));

        ExpectRedirect(response, "/Identity/Account/Manage/TwoFactorAuthentication");
        var saved = await FindAsync(user.Id);
        saved.TwoFactorEnabled.Should().BeFalse();
        saved.SecurityStamp.Should().NotBe(stamp);
        await ExpectAfterTheNextCookieCheckAsync(signedIn: client, signedOut: otherClient);
    }

    // Past the interval, every session's cookie is checked against the stamp again.
    private async Task ExpectAfterTheNextCookieCheckAsync(HttpClient signedIn, HttpClient signedOut)
    {
        try
        {
            _factory.Clock.Offset = TimeSpan.FromMinutes(2);
            (await signedIn.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK, "the session that made the change goes on");
            ExpectRedirect(await signedOut.GetAsync(SignedInPage), "/Identity/Account/Login");
        }
        finally
        {
            _factory.Clock.Offset = TimeSpan.Zero;
        }
    }

    private async Task<TestUser> CreateUserAsync(Setup setup)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        var email = $"two-factor-session-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Two",
            LastName = "Factor",
        };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        if (setup != Setup.NoAuthenticator)
        {
            (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        }
        if (setup == Setup.TwoFactorOn)
        {
            (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
            (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)).Should().NotBeNull();
        }
        return new TestUser(user.Id, email, await users.GetAuthenticatorKeyAsync(user), setup == Setup.TwoFactorOn);
    }

    // The password, then the authenticator code when two-factor is on.
    private async Task<HttpClient> SignedInClientAsync(TestUser user)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginPage = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", Form(loginPage,
            ("Input.Email", user.Email), ("Input.Password", Password), ("Input.RememberMe", "false")));
        if (!user.TwoFactorOn)
        {
            ExpectRedirect(response, "/Dashboard");
            return client;
        }

        ExpectRedirect(response, "/Identity/Account/LoginWith2fa");
        var codePage = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        response = await client.PostAsync("/Identity/Account/LoginWith2fa", Form(codePage,
            ("Input.TwoFactorCode", AuthenticatorApp.Code(user.AuthenticatorKey!)), ("Input.RememberMachine", "false")));
        ExpectRedirect(response, "/");
        return client;
    }

    private async Task<ApplicationUser> FindAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await Users(scope).FindByIdAsync(userId))!;
    }

    private async Task<string?> AuthenticatorKeyAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        return await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync(userId))!);
    }

    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    // The fields, plus the antiforgery token of a page loaded in the same session.
    private static FormUrlEncodedContent Form(string page, params (string Name, string Value)[] fields) =>
        new(fields.Select(f => KeyValuePair.Create(f.Name, f.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", Token(page))));

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    // Login redirects are absolute, the app's own are relative.
    private static void ExpectRedirect(HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        new Uri(BaseAddress, response.Headers.Location!).AbsolutePath.Should().Be(path);
    }

    private static UserManager<ApplicationUser> Users(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
}
