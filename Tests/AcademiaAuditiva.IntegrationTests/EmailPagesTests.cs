using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The account pages with e-mail on (production sends through Resend's SMTP
/// server) and off. Sign-up shows its confirmation link only while no e-mail
/// can go out, and a failed send never turns an account page into an error.
/// </summary>
public class EmailPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Email-Pages!Pass1";

    private static readonly Dictionary<string, string?> Resend = new()
    {
        ["Smtp:Host"] = "smtp.resend.com",
        ["Smtp:Port"] = "465",
        ["Smtp:User"] = "resend",
        ["Smtp:Password"] = "x",
        ["Smtp:FromAddress"] = "no-reply@academiaauditiva.com",
    };

    private readonly TestWebApplicationFactory _factory;

    public EmailPagesTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterConfirmation_ShowsTheLink_WhenNoEmailCanBeSent()
    {
        await using var app = App(new());
        var email = await CreateUserAsync(app, confirmed: false);

        (await RegisterConfirmationAsync(app, email)).Should().Contain("id=\"confirm-link\"");
    }

    [Fact]
    public async Task RegisterConfirmation_HidesTheLink_WhenEmailIsOn()
    {
        await using var app = App(Resend);
        var email = await CreateUserAsync(app, confirmed: false);

        (await RegisterConfirmationAsync(app, email)).Should().NotContain("confirm-link");
    }

    [Fact]
    public async Task RegisterConfirmation_ShowsTheLink_WhenTheSenderAddressIsMissing()
    {
        // Resend signs in as "resend", which is no address to send from.
        await using var app = App(new(Resend) { ["Smtp:FromAddress"] = "" });
        var email = await CreateUserAsync(app, confirmed: false);

        (await RegisterConfirmationAsync(app, email)).Should().Contain("id=\"confirm-link\"");
    }

    [Fact]
    public async Task Register_GoesOnToTheConfirmationPage_WhenTheEmailFails()
    {
        var sender = new RecordingEmailSender(fail: true);
        await using var app = App(Resend, sender);
        await EnsureRoleAsync(app, RoleNames.Student);
        var client = Client(app);
        var email = $"register-{Guid.NewGuid():N}@example.test";

        var page = await client.GetStringAsync("/Identity/Account/Register");
        var response = await client.PostAsync("/Identity/Account/Register", Form(page, new()
        {
            ["Input.FirstName"] = "Email",
            ["Input.LastName"] = "Outage",
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Identity/Account/RegisterConfirmation?");
        sender.Recipients.Should().Equal(email);
    }

    [Fact]
    public async Task ForgotPassword_AnswersAsUsual_WhenTheEmailFails()
    {
        var sender = new RecordingEmailSender(fail: true);
        await using var app = App(Resend, sender);
        var email = await CreateUserAsync(app, confirmed: true);
        var client = Client(app);

        var page = await client.GetStringAsync("/Identity/Account/ForgotPassword");
        var response = await client.PostAsync("/Identity/Account/ForgotPassword",
            Form(page, new() { ["Input.Email"] = email }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Identity/Account/ForgotPasswordConfirmation");
        sender.Recipients.Should().Equal(email);
    }

    [Fact]
    public async Task ResendEmailConfirmation_AnswersAsUsual_WhenTheEmailFails()
    {
        var sender = new RecordingEmailSender(fail: true);
        await using var app = App(Resend, sender);
        var email = await CreateUserAsync(app, confirmed: false);
        var client = Client(app);

        var page = await client.GetStringAsync("/Identity/Account/ResendEmailConfirmation");
        var response = await client.PostAsync("/Identity/Account/ResendEmailConfirmation",
            Form(page, new() { ["Input.Email"] = email }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Verification email sent. Please check your email.");
        sender.Recipients.Should().Equal(email);
    }

    [Theory]
    [InlineData("ChangeEmail", false, "Confirmation link to change email sent. Please check your email.")]
    [InlineData("ChangeEmail", true, "The email could not be sent. Please try again in a few minutes.")]
    [InlineData("SendVerificationEmail", false, "Verification email sent. Please check your email.")]
    [InlineData("SendVerificationEmail", true, "The email could not be sent. Please try again in a few minutes.")]
    public async Task ManageEmail_SaysWhetherTheEmailWentOut(string handler, bool fail, string message)
    {
        var sender = new RecordingEmailSender(fail);
        await using var app = App(Resend, sender);
        var email = await CreateUserAsync(app, confirmed: true);
        var client = Client(app);
        await SignInAsync(client, email);
        var newEmail = $"new-{Guid.NewGuid():N}@example.test";

        var page = await client.GetStringAsync("/Identity/Account/Manage/Email");
        var response = await client.PostAsync($"/Identity/Account/Manage/Email?handler={handler}",
            Form(page, new() { ["Input.NewEmail"] = newEmail }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/Manage/Email"));
        after.Should().Contain($"alert alert-{(fail ? "danger" : "success")} alert-dismissible");
        after.Should().Contain(message);
        sender.Recipients.Should().Equal(handler == "ChangeEmail" ? newEmail : email);
    }

    // The fixture's app with these settings on top, sending through a fake
    // so no test ever reaches a real mail server.
    private WebApplicationFactory<Program> App(Dictionary<string, string?> settings, RecordingEmailSender? sender = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender ?? new RecordingEmailSender(fail: false));
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> RegisterConfirmationAsync(WebApplicationFactory<Program> app, string email)
    {
        var response = await Client(app).GetAsync(
            $"/Identity/Account/RegisterConfirmation?email={Uri.EscapeDataString(email)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> CreateUserAsync(WebApplicationFactory<Program> app, bool confirmed)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"email-pages-{Guid.NewGuid():N}@example.test";
        var created = await users.CreateAsync(new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            FirstName = "Email",
            LastName = "Pages",
        }, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        return email;
    }

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", Form(page, new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "the account signs in");
    }

    private static async Task EnsureRoleAsync(WebApplicationFactory<Program> app, string role)
    {
        using var scope = app.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            (await roles.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
    }

    private static FormUrlEncodedContent Form(string page, Dictionary<string, string> fields)
    {
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        token.Success.Should().BeTrue("the page renders an antiforgery token");
        fields["__RequestVerificationToken"] = token.Groups[1].Value;
        return new FormUrlEncodedContent(fields);
    }

    private sealed class RecordingEmailSender(bool fail) : IEmailSender
    {
        public ConcurrentQueue<string> Recipients { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Recipients.Enqueue(email);
            return fail ? Task.FromException(new InvalidOperationException("SMTP is down")) : Task.CompletedTask;
        }
    }
}
