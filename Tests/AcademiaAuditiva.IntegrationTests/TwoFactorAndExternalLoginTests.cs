using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The two-factor pages carry the visitor's return address through a hidden field
/// and the recovery-code link, and the external-login callback must not show text
/// taken from the link. These walk the real pages: password, then authenticator or
/// recovery code, then the redirect back to the original address.
/// </summary>
public class TwoFactorAndExternalLoginTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Two-Factor!Pass1";

    // Needs HTML encoding inside attributes (" < ' &) and URL encoding inside links.
    private const string ReturnUrl = "/Home/Privacy?q=\"<b>a'b</b>&keySelect=D";

    private readonly TestWebApplicationFactory _factory;

    public TwoFactorAndExternalLoginTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task AuthenticatorCode_SendsTheVisitorBackToTheReturnUrl()
    {
        var (email, authenticatorKey, _) = await CreateTwoFactorUserAsync();
        var client = CreateClient();

        var form = TwoFactorForm(await client.GetStringAsync(await SignInWithPasswordAsync(client, email)));
        form.Should().NotContain("<b>", "the address must be HTML-encoded");
        HiddenValue(form, "returnUrl").Should().Be(ReturnUrl);

        // A wrong code shows the page again, which must still carry the address.
        var wrongCode = await PostTwoFactorAsync(client, form, "123456");
        wrongCode.StatusCode.Should().Be(HttpStatusCode.OK);
        form = TwoFactorForm(await wrongCode.Content.ReadAsStringAsync());
        HiddenValue(form, "returnUrl").Should().Be(ReturnUrl);

        var response = await PostTwoFactorAsync(client, form, AuthenticatorCode(authenticatorKey));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(ReturnUrl);
    }

    [Fact]
    public async Task RecoveryCodeLink_SendsTheVisitorBackToTheReturnUrl()
    {
        var (email, _, recoveryCode) = await CreateTwoFactorUserAsync();
        var client = CreateClient();
        var page = await client.GetStringAsync(await SignInWithPasswordAsync(client, email));

        var link = Regex.Match(page, "id=\"recovery-code-login\" href=\"([^\"]*)\"");
        link.Success.Should().BeTrue("the two-factor page links to the recovery-code page");
        var recoveryUrl = WebUtility.HtmlDecode(link.Groups[1].Value);
        recoveryUrl.Should().StartWith("/Identity/Account/LoginWithRecoveryCode?");
        QueryHelpers.ParseQuery(recoveryUrl[recoveryUrl.IndexOf('?')..])["returnUrl"].ToString()
            .Should().Be(ReturnUrl);

        // Like a browser, the recovery form posts back to its own address.
        var recoveryPage = await client.GetStringAsync(recoveryUrl);
        var response = await client.PostAsync(recoveryUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.RecoveryCode"] = recoveryCode,
            ["__RequestVerificationToken"] = Token(recoveryPage),
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(ReturnUrl);
    }

    [Fact]
    public async Task ExternalLoginCallback_DoesNotShowTextFromTheLink()
    {
        var client = CreateClient();

        var response = await client.GetAsync(
            "/Identity/Account/ExternalLogin?handler=Callback&returnUrl=%2FHome%2FPrivacy" +
            "&remoteError=Call%20555-0100%20to%20unlock%20your%20account");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginUrl = response.Headers.Location!.OriginalString;
        loginUrl.Should().StartWith("/Identity/Account/Login");
        var html = await client.GetStringAsync(loginUrl);
        html.Should().NotContain("555-0100")
            .And.Contain("Error loading external login information.");
    }

    private async Task<(string Email, string AuthenticatorKey, string RecoveryCode)> CreateTwoFactorUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"two-factor-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Two",
            LastName = "Factor",
        };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
        var authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);
        return (email, authenticatorKey!, recoveryCodes!.Single());
    }

    // Returns the two-factor page address the login page redirects to.
    private static async Task<string> SignInWithPasswordAsync(HttpClient client, string email)
    {
        var loginUrl = "/Identity/Account/Login?ReturnUrl=" + Uri.EscapeDataString(ReturnUrl);
        var loginPage = await client.GetStringAsync(loginUrl);
        var response = await client.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(loginPage),
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var twoFactorUrl = response.Headers.Location!.OriginalString;
        twoFactorUrl.Should().StartWith("/Identity/Account/LoginWith2fa?");
        return twoFactorUrl;
    }

    // Posts to the bare page address, so the return address can only come from the hidden field.
    private static Task<HttpResponseMessage> PostTwoFactorAsync(HttpClient client, string form, string code) =>
        client.PostAsync("/Identity/Account/LoginWith2fa", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["returnUrl"] = HiddenValue(form, "returnUrl")!,
            ["RememberMe"] = "False",
            ["Input.TwoFactorCode"] = code,
            ["Input.RememberMachine"] = "false",
            ["__RequestVerificationToken"] = Token(form),
        }));

    // The layout's language form has its own returnUrl field, so read only the two-factor form.
    private static string TwoFactorForm(string html)
    {
        var form = Regex.Matches(html, "<form\\b.*?</form>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .SingleOrDefault(f => f.Contains("name=\"Input.TwoFactorCode\"", StringComparison.Ordinal));
        form.Should().NotBeNull("the page renders the authenticator code form");
        return form!;
    }

    private static string? HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"<input type=\"hidden\" name=\"{Regex.Escape(name)}\" value=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    // The code an authenticator app shows: RFC 6238 with 30-second steps, HMAC-SHA1 and 6 digits.
    private static string AuthenticatorCode(string base32Key)
    {
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }
        var hash = HMACSHA1.HashData(Base32Decode(base32Key), counter);
        var offset = hash[^1] & 0x0F;
        var binary = (hash[offset] & 0x7F) << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(char.ToUpperInvariant(c));
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        return bytes.ToArray();
    }
}
