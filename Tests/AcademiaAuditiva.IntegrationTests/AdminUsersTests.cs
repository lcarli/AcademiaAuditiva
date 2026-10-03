using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Admin › Users locks, unlocks and deletes accounts. These sign in for real
/// (Identity cookie and security stamp) to check that a locked account can't
/// sign in, that sessions already open end at the next cookie check, and that
/// admins, the one signed in included, are refused.
/// </summary>
public class AdminUsersTests : IClassFixture<ClockedWebApplicationFactory>
{
    private const string Password = "Admin-Users!Pass1";

    // Any page that needs a signed-in user.
    private const string SignedInPage = "/Identity/Account/Manage";

    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly ClockedWebApplicationFactory _factory;

    public AdminUsersTests(ClockedWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Lock_BlocksSignInAndEndsOpenSessions()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var adminClient = await SignedInClientAsync(admin);
        var studentClient = await SignedInClientAsync(student);

        var response = await PostAsync(adminClient, $"/Admin/Users/Lock/{student.Id}");

        ExpectRedirect(response, "/Admin/Users");
        var list = await PageTextAsync(adminClient, "/Admin/Users?q=" + Uri.EscapeDataString(student.Email!));
        list.Should().Contain($"Account {student.Email} locked.", Exactly.Once(), "the message shows once")
            .And.Contain(">locked</span>")
            .And.Contain($"action=\"/Admin/Users/Unlock/{student.Id}\"")
            .And.NotContain($"action=\"/Admin/Users/Lock/{student.Id}\"");
        (await IsLockedOutAsync(student.Id)).Should().BeTrue();
        ExpectRedirect(await SignInAsync(NewClient(), student.Email!), "/Identity/Account/Lockout");

        // The open session lasts until its cookie is checked again, a minute at most.
        (await studentClient.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            _factory.Clock.Offset = TimeSpan.FromMinutes(2);
            ExpectRedirect(await studentClient.GetAsync(SignedInPage), "/Identity/Account/Login");
            (await adminClient.GetAsync("/Admin/Users")).StatusCode.Should().Be(HttpStatusCode.OK, "other sessions go on");
        }
        finally
        {
            _factory.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Lock_EndsASessionThatRefreshesItsSignIn()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var adminClient = await SignedInClientAsync(admin);
        var studentClient = await SignedInClientAsync(student);

        // Saving the profile refreshes the sign-in, and the session goes on...
        ExpectRedirect(await PostAsync(studentClient, SignedInPage), SignedInPage);
        (await studentClient.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);

        ExpectRedirect(await PostAsync(adminClient, $"/Admin/Users/Lock/{student.Id}"), "/Admin/Users");

        // ...until the account is locked: then it ends, instead of getting a cookie
        // with the new security stamp that every later check would accept.
        ExpectRedirect(await PostAsync(studentClient, SignedInPage), SignedInPage);
        ExpectRedirect(await studentClient.GetAsync(SignedInPage), "/Identity/Account/Login");
    }

    [Fact]
    public async Task AnAdminsLock_FailsTheNextCookieCheck_EvenWithTheSameSecurityStamp()
    {
        var student = await CreateUserAsync(RoleNames.Student);
        var studentClient = await SignedInClientAsync(student);
        using (var scope = _factory.Services.CreateScope())
        {
            // Locked as Admin › Users does, but keeping the security stamp.
            var users = Users(scope);
            var user = await users.FindByIdAsync(student.Id);
            (await users.SetLockoutEndDateAsync(user!, AdminLock.End)).Succeeded.Should().BeTrue();
        }

        (await studentClient.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            _factory.Clock.Offset = TimeSpan.FromMinutes(2);
            ExpectRedirect(await studentClient.GetAsync(SignedInPage), "/Identity/Account/Login");
        }
        finally
        {
            _factory.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task ALockedAccount_CantFinishSigningInWithARecoveryCode()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var recoveryCodes = await EnableTwoFactorAsync(student.Id);
        var adminClient = await SignedInClientAsync(admin);

        // Once the password is accepted, a recovery code finishes the sign-in...
        var before = NewClient();
        ExpectRedirect(await SignInAsync(before, student.Email!), "/Identity/Account/LoginWith2fa");
        ExpectRedirect(await RecoveryCodeSignInAsync(before, recoveryCodes[0]), "/");
        (await before.GetAsync(SignedInPage)).StatusCode.Should().Be(HttpStatusCode.OK);

        // ...but not if the account is locked in between, and the code stays unused.
        var after = NewClient();
        ExpectRedirect(await SignInAsync(after, student.Email!), "/Identity/Account/LoginWith2fa");
        ExpectRedirect(await PostAsync(adminClient, $"/Admin/Users/Lock/{student.Id}"), "/Admin/Users");
        ExpectRedirect(await RecoveryCodeSignInAsync(after, recoveryCodes[1]), "/Identity/Account/Lockout");
        ExpectRedirect(await after.GetAsync(SignedInPage), "/Identity/Account/Login");
        (await RecoveryCodesLeftAsync(student.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ALockedAccountsEmailChangeLink_DoesNotSignOutWhoeverOpensIt()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var adminClient = await SignedInClientAsync(admin);
        ExpectRedirect(await PostAsync(adminClient, $"/Admin/Users/Lock/{student.Id}"), "/Admin/Users");
        var newEmail = $"changed-{Guid.NewGuid():N}@example.test";
        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = Users(scope);
            var token = await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync(student.Id))!, newEmail);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }

        // Confirming the change refreshes the sign-in of the account it changed; the
        // session that opened the link belongs to someone else and must go on.
        var response = await adminClient.GetAsync(
            $"/Identity/Account/ConfirmEmailChange?userId={student.Id}&email={Uri.EscapeDataString(newEmail)}&code={code}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = _factory.Services.CreateScope())
        {
            (await Users(scope).FindByIdAsync(student.Id))!.Email.Should().Be(newEmail, "the link was used");
        }
        (await adminClient.GetAsync("/Admin/Users")).StatusCode.Should().Be(HttpStatusCode.OK, "the admin is still signed in");
    }

    [Fact]
    public async Task Unlock_LetsTheUserSignInAgain()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var adminClient = await SignedInClientAsync(admin);
        ExpectRedirect(await PostAsync(adminClient, $"/Admin/Users/Lock/{student.Id}"), "/Admin/Users");
        using (var scope = _factory.Services.CreateScope())
        {
            var users = Users(scope);
            (await users.AccessFailedAsync((await users.FindByIdAsync(student.Id))!)).Succeeded.Should().BeTrue();
        }

        var response = await PostAsync(adminClient, $"/Admin/Users/Unlock/{student.Id}");

        ExpectRedirect(response, "/Admin/Users");
        (await PageTextAsync(adminClient, "/Admin/Users")).Should().Contain($"Account {student.Email} unlocked.", Exactly.Once());
        using (var scope = _factory.Services.CreateScope())
        {
            var user = await Users(scope).FindByIdAsync(student.Id);
            user!.LockoutEnd.Should().BeNull();
            user.AccessFailedCount.Should().Be(0, "a fresh start, as if the account had never been locked");
        }
        ExpectRedirect(await SignInAsync(NewClient(), student.Email!), "/Dashboard");
    }

    [Fact]
    public async Task Admins_YourselfIncluded_CannotBeLockedOrDeleted()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var otherAdmin = await CreateUserAsync(RoleNames.Admin);
        var client = await SignedInClientAsync(admin);

        var refusals = new (Func<Task<HttpResponseMessage>> Send, string Message)[]
        {
            (() => PostAsync(client, $"/Admin/Users/Lock/{admin.Id}"), "You can't lock your own account."),
            (() => PostAsync(client, $"/Admin/Users/Lock/{otherAdmin.Id}"), "Admins can't be locked. Remove the Admin role first."),
            (() => client.GetAsync($"/Admin/Users/Delete/{admin.Id}"), "To delete your own account, use Manage account › Personal data."),
            (() => PostAsync(client, $"/Admin/Users/Delete/{admin.Id}"), "To delete your own account, use Manage account › Personal data."),
            (() => client.GetAsync($"/Admin/Users/Delete/{otherAdmin.Id}"), "Admins can't be deleted. Remove the Admin role first."),
            (() => PostAsync(client, $"/Admin/Users/Delete/{otherAdmin.Id}"), "Admins can't be deleted. Remove the Admin role first."),
        };
        foreach (var (send, message) in refusals)
        {
            ExpectRedirect(await send(), "/Admin/Users");
            (await PageTextAsync(client, "/Admin/Users")).Should().Contain(message, Exactly.Once());
        }

        foreach (var id in new[] { admin.Id, otherAdmin.Id })
        {
            (await ExistsAsync(id)).Should().BeTrue("admins are never deleted");
            (await IsLockedOutAsync(id)).Should().BeFalse("admins are never locked");
        }
    }

    [Fact]
    public async Task Delete_ShowsWhatGoesWithTheAccountAndThenRemovesIt()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Classrooms.Add(new Classroom { Name = "Choir", OwnerId = teacher.Id });
            db.Routines.Add(new Routine { Name = "Intervals", OwnerId = teacher.Id });
            await db.SaveChangesAsync();
        }
        var adminClient = await SignedInClientAsync(admin);
        var teacherClient = await SignedInClientAsync(teacher);

        var page = await PageTextAsync(adminClient, $"/Admin/Users/Delete/{teacher.Id}");
        page.Should().Contain(teacher.Email!)
            .And.Contain("Classrooms: 1").And.Contain("Routines: 1")
            .And.Contain("The classrooms and routines this person owns will be deleted too")
            .And.Contain($"action=\"/Admin/Users/Delete/{teacher.Id}\"");
        (await ExistsAsync(teacher.Id)).Should().BeTrue("the page only asks");

        var response = await PostAsync(adminClient, $"/Admin/Users/Delete/{teacher.Id}");

        ExpectRedirect(response, "/Admin/Users");
        (await PageTextAsync(adminClient, "/Admin/Users")).Should().Contain($"Account {teacher.Email} deleted.", Exactly.Once());
        (await ExistsAsync(teacher.Id)).Should().BeFalse();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Classrooms.AnyAsync(c => c.OwnerId == teacher.Id)).Should().BeFalse();
            (await db.Routines.AnyAsync(r => r.OwnerId == teacher.Id)).Should().BeFalse();
        }
        var signIn = await SignInAsync(NewClient(), teacher.Email!);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await signIn.Content.ReadAsStringAsync()).Should().Contain("Invalid login attempt.");

        try
        {
            _factory.Clock.Offset = TimeSpan.FromMinutes(2);
            ExpectRedirect(await teacherClient.GetAsync(SignedInPage), "/Identity/Account/Login");
        }
        finally
        {
            _factory.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task DeletePage_ForAStudent_HasNoTeachingWarning()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var client = await SignedInClientAsync(admin);

        var page = await PageTextAsync(client, $"/Admin/Users/Delete/{student.Id}");

        page.Should().Contain(student.Email!)
            .And.Contain("This permanently deletes the account")
            .And.NotContain("The classrooms and routines this person owns");
    }

    [Fact]
    public async Task UnknownAccounts_AreNotFound()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var client = await SignedInClientAsync(admin);

        (await PostAsync(client, "/Admin/Users/Lock/no-such-user")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Admin/Users/Unlock/no-such-user")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/Admin/Users/Delete/no-such-user")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Admin/Users/Delete/no-such-user")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Admin/Users/Lock")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyAdmins_CanLockUnlockOrDelete()
    {
        var student = await CreateUserAsync(RoleNames.Student);
        var other = await CreateUserAsync(RoleNames.Student);
        var studentClient = await SignedInClientAsync(student);
        var anonymous = NewClient();

        ExpectRedirect(await PostAsync(studentClient, $"/Admin/Users/Lock/{other.Id}"), "/Identity/Account/AccessDenied");
        ExpectRedirect(await PostAsync(studentClient, $"/Admin/Users/Unlock/{other.Id}"), "/Identity/Account/AccessDenied");
        ExpectRedirect(await studentClient.GetAsync($"/Admin/Users/Delete/{other.Id}"), "/Identity/Account/AccessDenied");
        ExpectRedirect(await PostAsync(studentClient, $"/Admin/Users/Delete/{other.Id}"), "/Identity/Account/AccessDenied");
        ExpectRedirect(await PostAsync(anonymous, $"/Admin/Users/Lock/{other.Id}"), "/Identity/Account/Login");
        ExpectRedirect(await anonymous.GetAsync($"/Admin/Users/Delete/{other.Id}"), "/Identity/Account/Login");
        ExpectRedirect(await PostAsync(anonymous, $"/Admin/Users/Delete/{other.Id}"), "/Identity/Account/Login");

        (await ExistsAsync(other.Id)).Should().BeTrue();
        (await IsLockedOutAsync(other.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Actions_NeedTheAntiforgeryToken()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        var client = await SignedInClientAsync(admin);

        (await PostAsync(client, $"/Admin/Users/Lock/{student.Id}", withToken: false)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, $"/Admin/Users/Delete/{student.Id}", withToken: false)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ExistsAsync(student.Id)).Should().BeTrue();
        (await IsLockedOutAsync(student.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task UsersList_OffersLockAndDeleteOnlyForNonAdmins_AndUnlockForAnyLockedAccount()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var lockedAdmin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        using (var scope = _factory.Services.CreateScope())
        {
            // As after too many wrong passwords or codes.
            var users = Users(scope);
            var user = await users.FindByIdAsync(lockedAdmin.Id);
            (await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded.Should().BeTrue();
        }
        var client = await SignedInClientAsync(admin);

        var page = await PageTextAsync(client, "/Admin/Users");

        page.Should().Contain($"action=\"/Admin/Users/Lock/{student.Id}\"")
            .And.Contain($"href=\"/Admin/Users/Delete/{student.Id}\"")
            .And.NotContain($"/Admin/Users/Unlock/{student.Id}\"");
        foreach (var id in new[] { admin.Id, lockedAdmin.Id })
        {
            page.Should().NotContain($"/Admin/Users/Lock/{id}\"").And.NotContain($"/Admin/Users/Delete/{id}\"");
        }
        page.Should().Contain($"action=\"/Admin/Users/Unlock/{lockedAdmin.Id}\"")
            .And.NotContain($"/Admin/Users/Unlock/{admin.Id}\"");

        ExpectRedirect(await PostAsync(client, $"/Admin/Users/Unlock/{lockedAdmin.Id}"), "/Admin/Users");
        (await IsLockedOutAsync(lockedAdmin.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task UsersList_ShowsALockoutAfterWrongSignIns_AndLockMakesItLast()
    {
        var admin = await CreateUserAsync(RoleNames.Admin);
        var student = await CreateUserAsync(RoleNames.Student);
        using (var scope = _factory.Services.CreateScope())
        {
            // As after five wrong passwords.
            var users = Users(scope);
            var user = await users.FindByIdAsync(student.Id);
            (await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(15))).Succeeded.Should().BeTrue();
        }
        var client = await SignedInClientAsync(admin);
        var list = "/Admin/Users?q=" + Uri.EscapeDataString(student.Email!);

        (await PageTextAsync(client, list)).Should().Contain("locked for 15 more min (failed sign-ins)")
            .And.NotContain(">locked</span>")
            .And.Contain($"action=\"/Admin/Users/Unlock/{student.Id}\"")
            .And.Contain($"action=\"/Admin/Users/Lock/{student.Id}\"");

        ExpectRedirect(await PostAsync(client, $"/Admin/Users/Lock/{student.Id}"), "/Admin/Users");

        (await PageTextAsync(client, list)).Should().Contain(">locked</span>")
            .And.NotContain("failed sign-ins")
            .And.Contain($"action=\"/Admin/Users/Unlock/{student.Id}\"")
            .And.NotContain($"action=\"/Admin/Users/Lock/{student.Id}\"");
        using (var scope = _factory.Services.CreateScope())
        {
            (await Users(scope).FindByIdAsync(student.Id))!.LockoutEnd.Should().Be(AdminLock.End);
        }
    }

    private async Task<ApplicationUser> CreateUserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            (await roles.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
        var users = Users(scope);
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = role,
            LastName = "Tester",
        };
        var created = await users.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> SignedInClientAsync(ApplicationUser user)
    {
        var client = NewClient();
        ExpectRedirect(await SignInAsync(client, user.Email!), "/Dashboard");
        return client;
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync("/Identity/Account/Login");
        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(page),
        }));
    }

    // Like the list's forms, with a token from a page loaded in the same session.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, bool withToken = true)
    {
        var form = new Dictionary<string, string>();
        if (withToken)
        {
            form["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Home/Privacy"));
        }
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static async Task<string> PageTextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    // Login and access-denied redirects are absolute, the app's own are relative.
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

    private async Task<bool> ExistsAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        return await Users(scope).FindByIdAsync(userId) != null;
    }

    private async Task<bool> IsLockedOutAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        return await users.IsLockedOutAsync((await users.FindByIdAsync(userId))!);
    }

    // An authenticator and two recovery codes, as set up on the account's two-factor page.
    private async Task<string[]> EnableTwoFactorAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        var user = (await users.FindByIdAsync(userId))!;
        (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.ToArray();
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

    private async Task<int> RecoveryCodesLeftAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = Users(scope);
        return await users.CountRecoveryCodesAsync((await users.FindByIdAsync(userId))!);
    }

    private static UserManager<ApplicationUser> Users(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
}
