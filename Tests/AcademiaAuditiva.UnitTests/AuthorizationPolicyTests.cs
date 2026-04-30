using System.Security.Claims;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Pin the Admin / Teacher / Student authorization policies that
/// <c>Program.cs</c> registers. Roles are hierarchical:
/// <list type="bullet">
///   <item>Admin can pass every policy.</item>
///   <item>Teacher can pass Teacher and Student.</item>
///   <item>Student can pass only Student.</item>
///   <item>Anonymous fails all three.</item>
/// </list>
/// These are exercised via the real <see cref="IAuthorizationService"/>
/// against a service collection configured the same way as production,
/// so a regression in <c>Program.cs</c> would surface as a test failure
/// rather than a 500 in production.
/// </summary>
public class AuthorizationPolicyTests
{
    private static IAuthorizationService BuildAuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(RoleNames.Admin,
                p => p.RequireRole(RoleNames.Admin));
            options.AddPolicy(RoleNames.Teacher,
                p => p.RequireRole(RoleNames.Admin, RoleNames.Teacher));
            options.AddPolicy(RoleNames.Student,
                p => p.RequireRole(RoleNames.Admin, RoleNames.Teacher, RoleNames.Student));
        });
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal UserWith(params string[] roles)
    {
        var identity = new ClaimsIdentity("test"); // IsAuthenticated when authType is set
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        foreach (var r in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, r));
        return new ClaimsPrincipal(identity);
    }

    [Theory]
    [InlineData(RoleNames.Admin,   true)]
    [InlineData(RoleNames.Teacher, false)]
    [InlineData(RoleNames.Student, false)]
    public async Task AdminPolicy_OnlyAdmin(string role, bool expected)
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWith(role), null, RoleNames.Admin);
        result.Succeeded.Should().Be(expected);
    }

    [Theory]
    [InlineData(RoleNames.Admin,   true)]
    [InlineData(RoleNames.Teacher, true)]
    [InlineData(RoleNames.Student, false)]
    public async Task TeacherPolicy_AdminAndTeacherPass(string role, bool expected)
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWith(role), null, RoleNames.Teacher);
        result.Succeeded.Should().Be(expected);
    }

    [Theory]
    [InlineData(RoleNames.Admin,   true)]
    [InlineData(RoleNames.Teacher, true)]
    [InlineData(RoleNames.Student, true)]
    public async Task StudentPolicy_AllRolesPass(string role, bool expected)
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWith(role), null, RoleNames.Student);
        result.Succeeded.Should().Be(expected);
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Teacher)]
    [InlineData(RoleNames.Student)]
    public async Task AnonymousUser_FailsEveryPolicy(string policy)
    {
        var auth = BuildAuthService();
        var anon = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type → not authenticated
        var result = await auth.AuthorizeAsync(anon, null, policy);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task UserWithMultipleRoles_PassesEveryPolicyTheyQualifyFor()
    {
        var auth = BuildAuthService();
        var dual = UserWith(RoleNames.Teacher, RoleNames.Student);

        (await auth.AuthorizeAsync(dual, null, RoleNames.Admin)).Succeeded.Should().BeFalse();
        (await auth.AuthorizeAsync(dual, null, RoleNames.Teacher)).Succeeded.Should().BeTrue();
        (await auth.AuthorizeAsync(dual, null, RoleNames.Student)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task UserWithUnrelatedRole_FailsEveryPolicy()
    {
        var auth = BuildAuthService();
        var hacker = UserWith("Maintainer"); // not a known role

        (await auth.AuthorizeAsync(hacker, null, RoleNames.Admin)).Succeeded.Should().BeFalse();
        (await auth.AuthorizeAsync(hacker, null, RoleNames.Teacher)).Succeeded.Should().BeFalse();
        (await auth.AuthorizeAsync(hacker, null, RoleNames.Student)).Succeeded.Should().BeFalse();
    }
}
