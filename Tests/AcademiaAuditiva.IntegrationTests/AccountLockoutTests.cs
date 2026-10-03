using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Five wrong passwords or authenticator codes in a row lock an account for 15
/// minutes. These sign in through the real pages to check that the lockout keeps
/// out even the right password or code, that it ends by itself or with a password
/// reset, and that it leaves the owner's open sessions alone, since anyone who
/// knows the account's address can cause it.
/// </summary>
public class AccountLockoutTests : IClassFixture<ClockedWebApplicationFactory>
{
    private const string Password = "Account-Lockout!Pass1";
    private const string WrongPassword = "Not-The-Password!1";
    private const string NewPassword = "Reset-Lockout!Pass2";
    private const string LockoutPage = "/Identity/Account/Lockout";

    // Any page that needs a signed-in user.
    private const string SignedInPage = "/Identity/Account/Manage";

    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly ClockedWebApplicationFactory _factory;

    public AccountLockoutTests(ClockedWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FiveWrongPasswordsInARow_LockTheAccountFor15Minutes()
    {
        var user = await CreateUserAsync();
        var client = NewClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await ExpectInvalidAttemptAsync(await SignInAsync(client, user.Email!, WrongPassword));
        }
        ExpectRedirect(await SignInAsync(client, user.Email!, WrongPassword), LockoutPage);

        // Not even the right password gets in now, from anywhere.
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, Password), LockoutPage);
        var locked = await FindAsync(user.Id);
        locked.LockoutEnd.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        locked.AccessFailedCount.Should().Be(0, "the next lockout takes five more");

        // Once the 15 minutes are over.
        await SetLockoutEndAsync(user.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, Password), "/Dashboard");
    }

    [Fact]
    public async Task ASuccessfulSignIn_StartsTheCountAgain()
    {
        var user = await CreateUserAsync();
        var client = NewClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await ExpectInvalidAttemptAsync(await SignInAsync(client, user.Email!, WrongPassword));
        }
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, Password), "/Dashboard");
        (await FindAsync(user.Id)).AccessFailedCount.Should().Be(0);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await ExpectInvalidAttemptAsync(await SignInAsync(client, user.Email!, WrongPassword));
        }
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, Password), "/Dashboard");
    }

    [Fact]
    public async Task ALockoutAfterWrongPasswords_LeavesTheOwnersOpenSessionsAlone()
    {
        var user = await CreateUserAsync();
        var owner = NewClient();
        ExpectRedirect(await SignInAsync(owner, user.Email!, Password), "/Dashboard");

        await LockOutWithWrongPasswordsAsync(user);

        (await owner.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            // Past the next cookie check, and through a refreshed sign-in (saving the profile).
            _factory.Clock.Offset = TimeSpan.FromMinutes(2);
            (await owner.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);
            ExpectRedirect(await PostAsync(owner, SignedInPage), SignedInPage);
            (await owner.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _factory.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task FiveWrongAuthenticatorCodes_LockTheAccount_AndNoCodeGetsPastIt()
    {
        var user = await CreateUserAsync();
        var (authenticatorKey, recoveryCodes) = await EnableTwoFactorAsync(user.Id);
        var client = NewClient();
        ExpectRedirect(await SignInAsync(client, user.Email!, Password), "/Identity/Account/LoginWith2fa");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrongCode = await AuthenticatorSignInAsync(client, "123456");
            wrongCode.StatusCode.Should().Be(HttpStatusCode.OK);
            (await wrongCode.Content.ReadAsStringAsync()).Should().Contain("Invalid authenticator code.");
        }
        ExpectRedirect(await AuthenticatorSignInAsync(client, "123456"), LockoutPage);

        ExpectRedirect(await AuthenticatorSignInAsync(client, AuthenticatorApp.Code(authenticatorKey)), LockoutPage);
        ExpectRedirect(await RecoveryCodeSignInAsync(client, recoveryCodes[0]), LockoutPage);
        ExpectRedirect(await client.GetAsync(SignedInPage), "/Identity/Account/Login");
        (await RecoveryCodesLeftAsync(user.Id)).Should().Be(2, "the refused code stays unused");
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, Password), LockoutPage);
    }

    [Fact]
    public async Task APasswordReset_EndsALockoutAfterWrongPasswords()
    {
        var user = await CreateUserAsync();
        await LockOutWithWrongPasswordsAsync(user);

        ExpectRedirect(await ResetPasswordAsync(user), "/Identity/Account/ResetPasswordConfirmation");

        (await FindAsync(user.Id)).LockoutEnd.Should().BeNull();
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, NewPassword), "/Dashboard");
    }

    [Fact]
    public async Task APasswordReset_StartsTheCountAgain()
    {
        var user = await CreateUserAsync();
        var client = NewClient();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await ExpectInvalidAttemptAsync(await SignInAsync(client, user.Email!, WrongPassword));
        }

        ExpectRedirect(await ResetPasswordAsync(user), "/Identity/Account/ResetPasswordConfirmation");

        (await FindAsync(user.Id)).AccessFailedCount.Should().Be(0, "one typo after the reset doesn't lock the account");
    }

    [Fact]
    public async Task APasswordReset_LeavesAnAdminsLock()
    {
        var user = await CreateUserAsync();
        await SetLockoutEndAsync(user.Id, AdminLock.End);

        ExpectRedirect(await ResetPasswordAsync(user), "/Identity/Account/ResetPasswordConfirmation");

        (await FindAsync(user.Id)).LockoutEnd.Should().Be(AdminLock.End);
        ExpectRedirect(await SignInAsync(NewClient(), user.Email!, NewPassword), LockoutPage);
    }

    [Fact]
    public async Task LockoutPage_SaysWhenTheAccountUnlocks_AndLinksToThePasswordReset()
    {
        var response = await NewClient().GetAsync(LockoutPage);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Should()
            .Contain("This account is locked and can't sign in right now.")
            .And.Contain("After too many wrong passwords or codes, an account unlocks by itself after 15 minutes, or as soon as its password is reset.")
            .And.Contain("id=\"lockout-reset-password\" href=\"/Identity/Account/ForgotPassword\"");
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var email = $"lockout-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Lockout",
            LastName = "Tester",
        };
        var created = await Users(scope).CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Identity/Account/Login");
        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(page),
        }));
    }

    private static async Task ExpectInvalidAttemptAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid login attempt.");
    }

    // As someone guessing the password would, from another browser.
    private async Task LockOutWithWrongPasswordsAsync(ApplicationUser user)
    {
        var client = NewClient();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await ExpectInvalidAttemptAsync(await SignInAsync(client, user.Email!, WrongPassword));
        }
        ExpectRedirect(await SignInAsync(client, user.Email!, WrongPassword), LockoutPage);
    }

    // After the password, on the page the sign-in sent the client to.
    private static async Task<HttpResponseMessage> AuthenticatorSignInAsync(HttpClient client, string code)
    {
        var page = await client.GetStringAsync("/Identity/Account/LoginWith2fa");
        return await client.PostAsync("/Identity/Account/LoginWith2fa", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["RememberMe"] = "False",
            ["Input.TwoFactorCode"] = code,
            ["Input.RememberMachine"] = "false",
            ["__RequestVerificationToken"] = Token(page),
        }));
    }

    private static async Task<HttpResponseMessage> RecoveryCodeSignInAsync(HttpClient client, string recoveryCode)
    {
        var page = await client.GetStringAsync("/Identity/Account/LoginWithRecoveryCode");
        return await client.PostAsync("/Identity/Account/LoginWithRecoveryCode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.RecoveryCode"] = recoveryCode,
            ["__RequestVerificationToken"] = Token(page),
        }));
    }

    // Follows the e-mailed link and sets NewPassword.
    private async Task<HttpResponseMessage> ResetPasswordAsync(ApplicationUser user)
    {
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = Users(scope);
            token = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(user.Id))!);
        }
        var client = NewClient();
        var page = await client.GetStringAsync(
            "/Identity/Account/ResetPassword?code=" + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
        return await client.PostAsync("/Identity/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
            ["Input.Code"] = token,
            ["__RequestVerificationToken"] = Token(page),
        }));
    }

    // Like the account's own forms, with a token from a page loaded in the same session.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Home/Privacy")),
        };
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    // Login redirects are absolute, the app's own are relative.
    private static void ExpectRedirect(HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        new Uri(BaseAddress, response.Headers.Location!).AbsolutePath.Should().Be(path);
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    private async Task<ApplicationUser> FindAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await Users(scope).FindByIdAsync(userId))!;
    }

    private async Task SetLockoutEndAsync(string userId, DateTimeOffset end)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        (await users.SetLockoutEndDateAsync((await users.FindByIdAsync(userId))!, end)).Succeeded.Should().BeTrue();
    }

    // An authenticator and two recovery codes, as set up on the account's two-factor page.
    private async Task<(string AuthenticatorKey, string[] RecoveryCodes)> EnableTwoFactorAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        var user = (await users.FindByIdAsync(userId))!;
        (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
        var authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2);
        return (authenticatorKey!, recoveryCodes!.ToArray());
    }

    private async Task<int> RecoveryCodesLeftAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        return await users.CountRecoveryCodesAsync((await users.FindByIdAsync(userId))!);
    }

    private static UserManager<ApplicationUser> Users(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
}
