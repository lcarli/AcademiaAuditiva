using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
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
/// can go out, a failed send never turns an account page into an error, and
/// every e-mail carries its link in the HTML and in the plain text.
/// </summary>
public class EmailPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Email-Pages!Pass1";

    // Typed by teachers, shown as text.
    private const string ClassroomName = "Ear & <Rhythm> \"A\"";

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
        ShouldLinkTo(sender.Sent.Single().Message, "/Identity/Account/ConfirmEmail");
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
        ShouldLinkTo(sender.Sent.Single().Message, "/Identity/Account/ResetPassword");
    }

    [Fact]
    public async Task Emails_SpeakTheLanguageOfThePage()
    {
        var sender = new RecordingEmailSender(fail: false);
        await using var app = App(Resend, sender);
        var email = await CreateUserAsync(app, confirmed: true);
        var client = Client(app);

        var page = await client.GetStringAsync("/Identity/Account/ForgotPassword?culture=pt-BR");
        await client.PostAsync("/Identity/Account/ForgotPassword?culture=pt-BR",
            Form(page, new() { ["Input.Email"] = email }));

        var message = sender.Sent.Should().ContainSingle().Subject.Message;
        message.Subject.Should().Be("Redefinir senha");
        message.HtmlBody.Should().Contain("<html lang=\"pt-BR\"");
        WebUtility.HtmlDecode(message.HtmlBody).Should().Contain(">Redefina sua senha</h1>");
        message.TextBody.Should().StartWith("Redefina sua senha\n");
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
        ShouldLinkTo(sender.Sent.Single().Message, "/Identity/Account/ConfirmEmail");
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
        ShouldLinkTo(sender.Sent.Single().Message,
            handler == "ChangeEmail" ? "/Identity/Account/ConfirmEmailChange" : "/Identity/Account/ConfirmEmail");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassroomInvite_SendsTheLink_OrShowsItToTheTeacherWhenTheEmailFails(bool fail)
    {
        var sender = new RecordingEmailSender(fail);
        await using var app = App(Resend, sender);
        var (teacher, classroomId) = await CreateTeacherWithClassroomAsync(app);
        var client = Client(app);
        await SignInAsync(client, teacher);
        var student = $"student-{Guid.NewGuid():N}@example.test";

        var page = await client.GetStringAsync($"/Teacher/Members/Invite?classroomId={classroomId}");
        var response = await client.PostAsync("/Teacher/Members/Invite",
            Form(page, new() { ["Email"] = student, ["ClassroomId"] = classroomId.ToString() }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var (to, message) = sender.Sent.Should().ContainSingle().Subject;
        to.Should().Be(student);
        var link = ShouldLinkTo(message, "/invite/accept");
        message.Subject.Should().Contain(ClassroomName);
        WebUtility.HtmlDecode(message.HtmlBody).Should().Contain(ClassroomName)
            .And.Contain($"Email Pages ({teacher})", "students recognize the teacher by name or address");
        message.TextBody.Should().Contain(ClassroomName).And.Contain($"Email Pages ({teacher})");

        var details = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.OriginalString));
        if (fail)
        {
            details.Should().Contain(link, "the teacher can pass the link on");
        }
        else
        {
            details.Should().Contain(student).And.NotContain(link);
        }
    }

    // The fixture's app with these settings on top, sending through a fake
    // so no test ever reaches a real mail server.
    private WebApplicationFactory<Program> App(Dictionary<string, string?> settings, RecordingEmailSender? sender = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                var fake = sender ?? new RecordingEmailSender(fail: false);
                services.RemoveAll<IEmailMessageSender>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailMessageSender>(fake);
                services.AddSingleton<IEmailSender>(fake);
            });
        });

    // The button and the copyable link in the HTML lead to one page, which the text gives too.
    private static string ShouldLinkTo(EmailMessage message, string path)
    {
        var links = Regex.Matches(message.HtmlBody, "\\shref=\"([^\"]*)\"")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Where(href => href != "https://academiaauditiva.com")
            .ToList();
        links.Should().HaveCount(2).And.AllBeEquivalentTo(links[0]);
        new Uri(links[0]).AbsolutePath.Should().Be(path);
        message.TextBody.Should().Contain(links[0]);
        return links[0];
    }

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

    private static async Task<(string Email, int ClassroomId)> CreateTeacherWithClassroomAsync(WebApplicationFactory<Program> app)
    {
        var email = await CreateUserAsync(app, confirmed: true);
        await EnsureRoleAsync(app, RoleNames.Teacher);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var teacher = await users.FindByEmailAsync(email);
        (await users.AddToRoleAsync(teacher!, RoleNames.Teacher)).Succeeded.Should().BeTrue();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var classroom = new Classroom { Name = ClassroomName, OwnerId = teacher!.Id };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        return (email, classroom.Id);
    }

    private static FormUrlEncodedContent Form(string page, Dictionary<string, string> fields)
    {
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        token.Success.Should().BeTrue("the page renders an antiforgery token");
        fields["__RequestVerificationToken"] = token.Groups[1].Value;
        return new FormUrlEncodedContent(fields);
    }

    private sealed class RecordingEmailSender(bool fail) : IEmailMessageSender, IEmailSender
    {
        public ConcurrentQueue<(string To, EmailMessage Message)> Sent { get; } = new();

        public IEnumerable<string> Recipients => Sent.Select(s => s.To);

        public Task SendEmailAsync(string email, EmailMessage message)
        {
            Sent.Enqueue((email, message));
            return fail ? Task.FromException(new InvalidOperationException("SMTP is down")) : Task.CompletedTask;
        }

        // Only Identity's own pages send bare HTML, and the site replaces every one that sends e-mail.
        public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
            throw new InvalidOperationException("The site's pages send composed messages.");
    }
}
