using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A routine closes at the end of its due date, unless the teacher who assigns it accepts
/// late answers; without a due date it never closes, so there is nothing to accept late.
/// </summary>
public class RoutineAssignTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Routine-Assign!Pass1";
    private const string AllowLateHelp =
        "Students can still answer after the due date. Otherwise the routine closes at the end of that day.";

    private readonly TestWebApplicationFactory _factory;

    public RoutineAssignTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AssignForm_OffersToAcceptLateAnswers()
    {
        var seeded = await SeedAsync();
        var client = await SignedInClientAsync(seeded.Teacher);

        var html = await PageAsync(client, $"/Teacher/Routines/Assign?routineId={seeded.RoutineId}");

        html.Should().MatchRegex("<input class=\"form-check-input\" aria-describedby=\"allowLateHelp\" type=\"checkbox\"[^>]*id=\"AllowLate\" name=\"AllowLate\" value=\"true\"")
            .And.MatchRegex("<label class=\"form-check-label\" for=\"AllowLate\">Accept late answers</label>")
            .And.Contain($"<div id=\"allowLateHelp\" class=\"form-text\">{AllowLateHelp}</div>");
    }

    [Fact]
    public async Task Assign_WithADueDate_CanAcceptLateAnswers()
    {
        var seeded = await SeedAsync();
        var client = await SignedInClientAsync(seeded.Teacher);

        var details = await AssignAsync(client, seeded, dueAt: "2026-02-01", allowLate: true);

        var assignment = await AssignmentAsync(seeded.RoutineId);
        assignment.DueAt.Should().Be(new DateTime(2026, 2, 1));
        assignment.AllowLate.Should().BeTrue();
        var html = await PageAsync(client, details);
        html.Should().Contain("· due 2026-02-01 · Late answers accepted");
    }

    [Fact]
    public async Task Assign_WithADueDate_ClosesAtTheEndOfItByDefault()
    {
        var seeded = await SeedAsync();
        var client = await SignedInClientAsync(seeded.Teacher);

        var details = await AssignAsync(client, seeded, dueAt: "2026-02-01", allowLate: false);

        (await AssignmentAsync(seeded.RoutineId)).AllowLate.Should().BeFalse();
        (await PageAsync(client, details)).Should().Contain("· due 2026-02-01").And.NotContain("Late answers accepted");
    }

    [Fact]
    public async Task Assign_WithoutADueDate_HasNoLateAnswersToAccept()
    {
        var seeded = await SeedAsync();
        var client = await SignedInClientAsync(seeded.Teacher);

        var details = await AssignAsync(client, seeded, dueAt: null, allowLate: true);

        var assignment = await AssignmentAsync(seeded.RoutineId);
        assignment.DueAt.Should().BeNull();
        assignment.AllowLate.Should().BeFalse();
        (await PageAsync(client, details)).Should().NotContain("Late answers accepted");
    }

    private sealed record Seeded(ApplicationUser Teacher, string StudentId, int RoutineId);

    // A teacher with a routine, and a classroom with a student to assign it to.
    private async Task<Seeded> SeedAsync()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var student = await CreateUserAsync(RoleNames.Student);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
        var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
        var routine = new Routine
        {
            Name = "Notes",
            OwnerId = teacher.Id,
            Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 5 } }
        };
        db.Routines.Add(routine);
        db.Classrooms.Add(new Classroom
        {
            Name = "Choir",
            OwnerId = teacher.Id,
            Members = { new ClassroomMember { StudentId = student.Id } }
        });
        await db.SaveChangesAsync();
        return new Seeded(teacher, student.Id, routine.Id);
    }

    // Assigns the routine to the student alone and returns the routine page it redirects to.
    private static async Task<string> AssignAsync(HttpClient client, Seeded seeded, string? dueAt, bool allowLate)
    {
        var form = await PageAsync(client, $"/Teacher/Routines/Assign?routineId={seeded.RoutineId}");
        var fields = new Dictionary<string, string>
        {
            ["RoutineId"] = seeded.RoutineId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Target"] = "student",
            ["StudentId"] = seeded.StudentId,
            ["DueAt"] = dueAt ?? string.Empty,
            ["__RequestVerificationToken"] = Token(form),
        };
        if (allowLate) fields["AllowLate"] = "true";

        var response = await client.PostAsync("/Teacher/Routines/Assign", new FormUrlEncodedContent(fields));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var details = response.Headers.Location!.OriginalString;
        details.Should().Be($"/Teacher/Routines/Details/{seeded.RoutineId}");
        return details;
    }

    private async Task<RoutineAssignment> AssignmentAsync(int routineId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RoutineAssignments.AsNoTracking().SingleAsync(a => a.RoutineId == routineId);
    }

    private async Task<ApplicationUser> CreateUserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            (await roles.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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

    private async Task<HttpClient> SignedInClientAsync(ApplicationUser user)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }
}
