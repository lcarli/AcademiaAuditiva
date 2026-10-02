using System.Globalization;
using System.Net;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Resources;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Exercises are stored by identifier (HigherOrLower) and form labels are resource
/// keys in [Display] attributes; pages show both in the page's language.
/// </summary>
public class LocalizedNamesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Localized-Names!Pass1";

    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly TestWebApplicationFactory _factory;

    public LocalizedNamesTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en-US", "Higher or Lower")]
    [InlineData("pt-BR", "Mais alto ou mais grave")]
    [InlineData("fr-CA", "Plus aigu ou plus grave")]
    public async Task StudentDashboard_HasTheNameOfEveryExerciseBeforeItsListsLoad(string culture, string higherOrLower)
    {
        var catalogue = await EnsureExercisesAsync();
        var client = await SignedInClientAsync(await CreateUserAsync(RoleNames.Student));

        var html = await PageAsync(client, $"/Dashboard?culture={culture}");

        // The history and most-missed lists load afterwards, naming exercises by identifier.
        var map = Regex.Match(html, "<script id=\"aa-i18n-map\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
        map.Success.Should().BeTrue("the page carries its translations");
        var names = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(map.Groups[1].Value)!["exercises"];
        names.Keys.Should().BeEquivalentTo(catalogue);
        var resources = new ResourceManager(typeof(SharedResources));
        foreach (var (id, name) in names)
        {
            name.Should().Be(resources.GetString(id, CultureInfo.GetCultureInfo(culture)), "{0} needs a {1} name", id, culture)
                .And.NotBe(id);
        }
        names["HigherOrLower"].Should().Be(higherOrLower);
        html.Should().NotContain("GetExerciseTranslations", "no list waits for another request to name exercises");
    }

    [Theory]
    [InlineData("pt-BR", "Mais alto ou mais grave")]
    [InlineData("fr-CA", "Plus aigu ou plus grave")]
    public async Task TeacherStudentPage_NamesExercisesInThePageLanguage(string culture, string higherOrLower)
    {
        await EnsureExercisesAsync();
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var student = await CreateUserAsync(RoleNames.Student);
        int classroomId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var classroom = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            db.Classrooms.Add(classroom);
            await db.SaveChangesAsync();
            classroomId = classroom.Id;
            var exercise = await db.Exercises.SingleAsync(e => e.Name == "HigherOrLower");
            db.AddRange(
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id },
                new ScoreAggregate
                {
                    UserId = student.Id,
                    ExerciseId = exercise.ExerciseId,
                    CorrectCount = 3,
                    ErrorCount = 1,
                    BestScore = 3,
                    LastAttemptAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }
        var client = await SignedInClientAsync(teacher);

        var page = WebUtility.HtmlDecode(await PageAsync(client, $"/Teacher/Dashboard/Student/{student.Id}?classroomId={classroomId}&culture={culture}"));

        page.Should().Contain($"<td>{higherOrLower}</td>").And.NotContain(">HigherOrLower<");
    }

    [Theory]
    [InlineData("pt-BR", "Nome", "Descrição", "O campo Nome é obrigatório.")]
    [InlineData("fr-CA", "Nom", "Description", "Le champ Nom est obligatoire.")]
    public async Task RoutineForm_LabelsAndMessagesUseThePageLanguage(string culture, string name, string description, string nameRequired)
    {
        var client = await SignedInClientAsync(await CreateUserAsync(RoleNames.Teacher));

        var page = WebUtility.HtmlDecode(await PageAsync(client, $"/Teacher/Routines/Create?culture={culture}"));

        page.Should().MatchRegex($"<label[^>]*for=\"Name\"[^>]*>{Regex.Escape(name)}</label>")
            .And.MatchRegex($"<label[^>]*for=\"Description\"[^>]*>{Regex.Escape(description)}</label>")
            .And.Contain($"data-val-required=\"{nameRequired}\"");
    }

    [Theory]
    [InlineData("en-US", "Username")]
    [InlineData("pt-BR", "Nome de usuário")]
    [InlineData("fr-CA", "Nom d’utilisateur")]
    public async Task ProfilePage_UsernameLabelUsesThePageLanguage(string culture, string label)
    {
        var client = await SignedInClientAsync(await CreateUserAsync(RoleNames.Student));

        var page = WebUtility.HtmlDecode(await PageAsync(client, $"/Identity/Account/Manage?culture={culture}"));

        page.Should().MatchRegex($"<label[^>]*for=\"Username\"[^>]*>{Regex.Escape(label)}</label>");
    }

    // The real catalogue, so an exercise added without a name in every language fails here.
    private async Task<List<string>> EnsureExercisesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
        return await db.Exercises.Select(e => e.Name).ToListAsync();
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
        new Uri(BaseAddress, response.Headers.Location!).AbsolutePath.Should().Be("/Dashboard");
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
