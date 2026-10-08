using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Email;
using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A routine goes to one of the teacher's classrooms, to the whole class or to the students the
/// teacher ticks (#108), and those students are e-mailed about it in the background: each in their
/// own language, within the day's limit, and none who hasn't confirmed their address or who turned
/// these e-mails off on the Notifications page.
/// </summary>
public class RoutineEmailTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Routine-Email!Pass1";
    private const string Assigned = "Routine assigned.";
    private const string Emailed = "Routine assigned. We're emailing the students about it.";
    private const string EmailFailed = "The students couldn't be emailed about this routine. They'll still find it in My Training.";
    private const string EmailNote = "Students who confirmed their email address get an email about the routine, unless they turned these emails off.";
    private const string NotificationsUrl = "/Identity/Account/Manage/Notifications";
    private const string Unconfirmed = "Your email address isn't confirmed yet, so these emails can't be sent to you.";

    // Production sends through Resend's SMTP server. The fixture's apps share one database, so one
    // count of the day's e-mails: hence a limit no test here reaches.
    private static readonly Dictionary<string, string?> EmailOn = new()
    {
        ["Smtp:Host"] = "smtp.resend.com",
        ["Smtp:Port"] = "465",
        ["Smtp:User"] = "resend",
        ["Smtp:Password"] = "x",
        ["Smtp:FromAddress"] = "no-reply@academiaauditiva.com",
        ["NotificationEmails:DailyLimit"] = "1000",
    };

    private readonly TestWebApplicationFactory _factory;

    public RoutineEmailTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssignForm_OffersTheTeachersOpenClassrooms_EachWithItsStudents(bool emailOn)
    {
        await using var app = App(_factory, emailOn ? EmailOn : new());
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var routineId = await RoutineAsync(app, teacher);
        var choir = await ClassroomAsync(app, teacher, "Choir", [bruno, ana]);
        var band = await ClassroomAsync(app, teacher, "Band");
        await ClassroomAsync(app, teacher, "Attic", [ana], archived: true);
        await ClassroomAsync(app, await CreateUserAsync(app, RoleNames.Teacher, "other"), "Elsewhere", [ana]);
        var client = await SignedInClientAsync(app, teacher);

        var page = await PageAsync(client, $"/Teacher/Routines/Assign?routineId={routineId}");

        ClassroomOptions(page).Should().Equal(["", Id(band), Id(choir)], "the teacher's open classrooms, by name");
        var lists = StudentLists(page);
        lists.Keys.Should().BeEquivalentTo([band, choir]);
        lists.Values.Should().OnlyContain(l => !l.Shown, "a classroom's students show once it is picked and the teacher chooses students");
        lists[choir].Students.Should().Equal([(ana.Id, false), (bruno.Id, false)], "by name");
        WebUtility.HtmlDecode(lists[band].Html).Should().Contain("This classroom has no students yet.");
        Checked(page, "to-class").Should().BeTrue("the whole class gets the routine unless the teacher chooses");
        Checked(page, "to-chosen").Should().BeFalse();
        if (emailOn)
            WebUtility.HtmlDecode(page).Should().Contain(EmailNote);
        else
            WebUtility.HtmlDecode(page).Should().NotContain(EmailNote, "no e-mail goes out");
    }

    [Fact]
    public async Task TheWholeClass_IsEmailed_EachInTheirLanguage_ButNotWhoDoesntGetTheseEmails()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana", "en-US");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno", "pt-BR");
        var carla = await CreateUserAsync(app, RoleNames.Student, "carla", "fr-CA");
        var davi = await CreateUserAsync(app, RoleNames.Student, "davi");
        var elisa = await CreateUserAsync(app, RoleNames.Student, "elisa", confirmed: false);
        var fabio = await CreateUserAsync(app, RoleNames.Student, "fabio", emailsOff: true);
        var gabi = await CreateUserAsync(app, RoleNames.Student, "gabi");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [fabio, elisa, davi, carla, bruno, ana]);
        await ClassroomAsync(app, teacher, "Band", [gabi]);
        var client = await SignedInClientAsync(app, teacher);

        var details = await FollowAsync(client, await AssignAsync(client, routineId, classroomId, dueAt: "2026-11-20"), DetailsUrl(routineId));

        ShowsSuccess(details, Emailed);
        details.Should().NotContain("alert-danger");
        var sent = await SentAsync(app, sender);
        sent.Select(s => s.To).Should().Equal(Emails(ana, bruno, carla, davi),
            "the members who confirmed their address and get these e-mails, by user name");
        sent.Select(s => s.Message.Subject).Should().Equal(
            "New routine to practice: Notes",
            "Nova rotina para praticar: Notes",
            "Nouvelle routine à pratiquer\u00a0: Notes",
            "New routine to practice: Notes");
        sent.Select(s => Lang(s.Message)).Should().Equal(["en-US", "pt-BR", "fr-CA", "en-US"],
            "a student who never chose a language gets the teacher's");
        var teacherName = $"Teacher Tester ({teacher.Email})";
        sent[0].Message.TextBody.Should().StartWith(
            "You have a new routine\n\n" +
            $"{teacherName} assigned you this routine in the classroom Choir on Academia Auditiva:\nNotes\n\n" +
            "Open My Training\nhttps://academiaauditiva.com/MyTraining\n\n" +
            "Due on Friday, November 20, 2026. Late answers are still accepted after that.\n\n-- \n")
            .And.Contain("\nTurn off these emails\nhttps://academiaauditiva.com/Identity/Account/Manage/Notifications\n");
        sent[1].Message.TextBody.Should().Contain($"{teacherName} atribuiu esta rotina a você na turma Choir da Academia Auditiva:");
    }

    [Fact]
    public async Task StudentsWhoNeverChoseALanguage_GetTheTeachers()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana", "en-US");
        var davi = await CreateUserAsync(app, RoleNames.Student, "davi");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, davi]);
        var client = await SignedInClientAsync(app, teacher);

        var response = await AssignAsync(client, routineId, classroomId, query: "?culture=fr-CA");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var sent = await SentAsync(app, sender);
        sent.Select(s => (s.To, Lang(s.Message))).Should().Equal([(ana.Email!, "en-US"), (davi.Email!, "fr-CA")]);
        sent[1].Message.TextBody.Should().StartWith("Vous avez une nouvelle routine\n")
            .And.Contain("Aucune date limite\u00a0: pratiquez à votre rythme.");
    }

    [Fact]
    public async Task ChosenStudents_AloneGetTheRoutine_AndTheEmails()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var carla = await CreateUserAsync(app, RoleNames.Student, "carla");
        var routineId = await RoutineAsync(app, teacher, "Duet");
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, bruno, carla]);
        var client = await SignedInClientAsync(app, teacher);

        var response = await AssignAsync(client, routineId, classroomId, "chosen", [carla.Id, ana.Id, carla.Id, ""]);

        var details = await FollowAsync(client, response, DetailsUrl(routineId));
        ShowsSuccess(details, Emailed);
        details.Should().Contain($"Chosen students: {ana.UserName}, {carla.UserName}");
        var assignment = await DbAsync(app, db => db.RoutineAssignments.AsNoTracking().Include(a => a.ChosenStudents)
            .SingleAsync(a => a.RoutineId == routineId));
        assignment.ClassroomId.Should().Be(classroomId);
        assignment.ChosenStudentsOnly.Should().BeTrue();
        assignment.ChosenStudents.Select(s => s.StudentId).Should().BeEquivalentTo([ana.Id, carla.Id], "each student once, and no blank");
        (await SentAsync(app, sender)).Select(s => s.To).Should().Equal(Emails(ana, carla));

        var dora = await CreateUserAsync(app, RoleNames.Student, "dora");
        await JoinAsync(app, classroomId, dora);
        (await RoutinesOfAsync(app, ana)).Should().Contain("Duet");
        (await RoutinesOfAsync(app, carla)).Should().Contain("Duet");
        (await RoutinesOfAsync(app, bruno)).Should().NotContain("Duet", "he wasn't chosen");
        (await RoutinesOfAsync(app, dora)).Should().NotContain("Duet", "she joined after the teacher chose");
    }

    [Fact]
    public async Task ChoosingAStudentFromAnotherClassroom_IsRefused()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var gabi = await CreateUserAsync(app, RoleNames.Student, "gabi");
        var routineId = await RoutineAsync(app, teacher);
        var choir = await ClassroomAsync(app, teacher, "Choir", [ana, bruno]);
        var band = await ClassroomAsync(app, teacher, "Band", [gabi]);
        var client = await SignedInClientAsync(app, teacher);

        var response = await AssignAsync(client, routineId, choir, "chosen", [ana.Id, gabi.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadAsStringAsync();
        WebUtility.HtmlDecode(page).Should().Contain("Every student you choose must be in the classroom.");
        Checked(page, "to-chosen").Should().BeTrue("the form comes back as the teacher left it");
        var lists = StudentLists(page);
        lists[choir].Shown.Should().BeTrue();
        lists[choir].Students.Should().Equal([(ana.Id, true), (bruno.Id, false)]);
        lists[band].Shown.Should().BeFalse();
        (await AssignmentCountAsync(app, routineId)).Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("none", "class", "Select a classroom.")]
    [InlineData("archived", "class", "Select a classroom.")]
    [InlineData("open", "chosen", "Choose at least one student.")]
    [InlineData("open", "everyone", "Invalid target.")]
    public async Task AnIncompleteAssignment_IsRefused(string classroom, string recipients, string error)
    {
        await using var app = App(_factory, EmailOn);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var routineId = await RoutineAsync(app, teacher);
        var open = await ClassroomAsync(app, teacher, "Choir", [ana]);
        var archived = await ClassroomAsync(app, teacher, "Attic", [ana], archived: true);
        var client = await SignedInClientAsync(app, teacher);
        int? classroomId = classroom switch { "open" => open, "archived" => archived, _ => null };

        var response = await AssignAsync(client, routineId, classroomId, recipients);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Should().Contain(error);
        (await AssignmentCountAsync(app, routineId)).Should().Be(0);
    }

    [Fact]
    public async Task AnotherTeachersClassroom_IsForbidden()
    {
        await using var app = App(_factory, EmailOn);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var other = await CreateUserAsync(app, RoleNames.Teacher, "other");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var routineId = await RoutineAsync(app, teacher);
        var theirs = await ClassroomAsync(app, other, "Elsewhere", [ana]);
        var client = await SignedInClientAsync(app, teacher);

        var response = await AssignAsync(client, routineId, theirs);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath.Should().Be("/Identity/Account/AccessDenied");
        (await AssignmentCountAsync(app, routineId)).Should().Be(0);
    }

    [Fact]
    public async Task PastTheDailyLimit_TheRoutineIsAssigned_WithoutTheEmails()
    {
        // A database of its own, so that the day's count starts at 0.
        await using var factory = new TestWebApplicationFactory();
        var clock = new AnswerTimeTests.ManualClock(TimeProvider.System.GetUtcNow());
        var sender = new RecordingEmailSender();
        await using var app = App(factory, new(EmailOn) { ["NotificationEmails:DailyLimit"] = "2" }, sender, clock);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var carla = await CreateUserAsync(app, RoleNames.Student, "carla");
        var first = await RoutineAsync(app, teacher, "First");
        var second = await RoutineAsync(app, teacher, "Second");
        var third = await RoutineAsync(app, teacher, "Third");
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, bruno, carla]);
        var client = await SignedInClientAsync(app, teacher);

        var details = await FollowAsync(client, await AssignAsync(client, first, classroomId), DetailsUrl(first));

        ShowsSuccess(details, Assigned);
        ShowsError(details, Limit(1, 3));
        (await SentAsync(app, sender)).Select(s => s.To).Should().Equal(Emails(ana, bruno));

        details = await FollowAsync(client, await AssignAsync(client, second, classroomId), DetailsUrl(second));

        ShowsSuccess(details, Assigned);
        ShowsError(details, Limit(3, 3));
        (await SentAsync(app, sender)).Should().HaveCount(2, "the day's e-mails are all gone");
        (await AssignmentCountAsync(app, second)).Should().Be(1, "the routine is assigned all the same");
        (await DbAsync(app, db => db.EmailDailyCounts.AsNoTracking().SingleAsync())).Sent.Should().Be(2);

        clock.Advance(TimeSpan.FromDays(1));
        details = await FollowAsync(client, await AssignAsync(client, third, classroomId), DetailsUrl(third));

        ShowsError(details, Limit(1, 3));
        (await SentAsync(app, sender)).Skip(2).Select(s => s.To).Should().Equal(Emails(ana, bruno), "a new day brings new e-mails");
    }

    [Fact]
    public async Task AFailedEmail_StopsNeitherTheOthers_NorTheAssignment()
    {
        var sender = new RecordingEmailSender(fail: true);
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, bruno]);
        var client = await SignedInClientAsync(app, teacher);

        var details = await FollowAsync(client, await AssignAsync(client, routineId, classroomId), DetailsUrl(routineId));

        ShowsSuccess(details, Emailed);
        (await SentAsync(app, sender)).Select(s => s.To).Should().Equal(Emails(ana, bruno), "each e-mail is tried");
        (await AssignmentCountAsync(app, routineId)).Should().Be(1);
    }

    [Fact]
    public async Task EmailsThatCantBeQueued_LeaveTheRoutineAssigned_AndTheTeacherIsTold()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender,
            services: s => s.ConfigureDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(new FailingDailyCount())));
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana]);
        var client = await SignedInClientAsync(app, teacher);

        var details = await FollowAsync(client, await AssignAsync(client, routineId, classroomId), DetailsUrl(routineId));

        ShowsSuccess(details, Assigned);
        ShowsError(details, EmailFailed);
        (await AssignmentCountAsync(app, routineId)).Should().Be(1);
        (await SentAsync(app, sender)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "60")]
    [InlineData(true, "0")]
    public async Task WithoutEmail_TheRoutineIsJustAssigned(bool smtp, string dailyLimit)
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, new(smtp ? EmailOn : []) { ["NotificationEmails:DailyLimit"] = dailyLimit }, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana]);
        var client = await SignedInClientAsync(app, teacher);

        var details = await FollowAsync(client, await AssignAsync(client, routineId, classroomId), DetailsUrl(routineId));

        ShowsSuccess(details, Assigned);
        details.Should().NotContain("alert-danger");
        (await AssignmentCountAsync(app, routineId)).Should().Be(1);
        app.Services.GetRequiredService<BackgroundEmailQueue>().Pending.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheEmails_SkipWhoStoppedGettingThemMeanwhile_AndAnUnassignedRoutine()
    {
        var sender = new RecordingEmailSender();
        await using var app = App(_factory, EmailOn, sender);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var carla = await CreateUserAsync(app, RoleNames.Student, "carla");
        var davi = await CreateUserAsync(app, RoleNames.Student, "davi");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, bruno, carla, davi]);
        var assignmentId = await AssignInDbAsync(app, routineId, classroomId);
        var job = new RoutineEmailJob(assignmentId, [ana.Id, bruno.Id, carla.Id, davi.Id], "en-US");
        await DbAsync(app, async db =>
        {
            (await db.Set<ApplicationUser>().SingleAsync(u => u.Id == bruno.Id)).RoutineEmailsOff = true;
            (await db.Set<ApplicationUser>().SingleAsync(u => u.Id == davi.Id)).EmailConfirmed = false;
            db.ClassroomMembers.Remove(await db.ClassroomMembers.SingleAsync(m => m.ClassroomId == classroomId && m.StudentId == carla.Id));
            return await db.SaveChangesAsync();
        });

        await SendAsync(app, job);

        sender.Recipients.Should().Equal(Emails(ana));

        await DbAsync(app, async db =>
        {
            db.RoutineAssignments.Remove(await db.RoutineAssignments.SingleAsync(a => a.Id == assignmentId));
            return await db.SaveChangesAsync();
        });
        await SendAsync(app, job);

        sender.Recipients.Should().Equal(Emails(ana), "a routine unassigned meanwhile is no news");
    }

    [Fact]
    public async Task OnlyTheChosenStudents_CanBeGivenAdjustments()
    {
        await using var app = App(_factory, new());
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "bruno");
        var routineId = await RoutineAsync(app, teacher);
        var classroomId = await ClassroomAsync(app, teacher, "Choir", [ana, bruno]);
        var assignmentId = await AssignInDbAsync(app, routineId, classroomId, [ana]);
        var client = await SignedInClientAsync(app, teacher);
        var url = $"/Teacher/Routines/Overrides?routineId={routineId}&assignmentId={assignmentId}";

        (await PageTextAsync(client, url)).Should().Contain($"studentId={ana.Id}").And.NotContain($"studentId={bruno.Id}");
        (await client.GetAsync($"{url}&studentId={ana.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{url}&studentId={bruno.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Teacher/Routines/Overrides",
            ("RoutineId", Id(routineId)), ("AssignmentId", Id(assignmentId)), ("StudentId", bruno.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Teacher/Routines/ClearOverrides",
            ("routineId", Id(routineId)), ("assignmentId", Id(assignmentId)), ("studentId", bruno.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/Teacher/Routines/ClearOverrides",
            ("routineId", Id(routineId)), ("assignmentId", Id(assignmentId)), ("studentId", ana.Id)))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task NotificationsPage_TurnsTheRoutineEmailsOffAndOn()
    {
        await using var app = App(_factory, new());
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var client = await SignedInClientAsync(app, ana);

        var page = await PageAsync(client, NotificationsUrl);

        Checked(page, "RoutineEmails").Should().BeTrue("they are on until the student turns them off");
        page.Should().Contain("class=\"nav-link active\" id=\"notifications\"");
        WebUtility.HtmlDecode(page).Should().Contain("Email me when a teacher assigns me a routine").And.NotContain(Unconfirmed);

        page = await FollowAsync(client, await PostAsync(client, NotificationsUrl, ("RoutineEmails", "false")), NotificationsUrl);

        page.Should().Contain("Your notification settings have been saved.");
        Checked(page, "RoutineEmails").Should().BeFalse();
        (await UserAsync(app, ana)).RoutineEmailsOff.Should().BeTrue();

        // A ticked box posts "true" before its hidden "false".
        page = await FollowAsync(client, await PostAsync(client, NotificationsUrl, ("RoutineEmails", "true"), ("RoutineEmails", "false")), NotificationsUrl);

        Checked(page, "RoutineEmails").Should().BeTrue();
        (await UserAsync(app, ana)).RoutineEmailsOff.Should().BeFalse();

        await DbAsync(app, async db =>
        {
            (await db.Set<ApplicationUser>().SingleAsync(u => u.Id == ana.Id)).EmailConfirmed = false;
            return await db.SaveChangesAsync();
        });
        (await PageTextAsync(client, NotificationsUrl)).Should().Contain(Unconfirmed);
    }

    [Fact]
    public async Task TheLanguageChosenOnTheSite_IsKeptForTheEmails()
    {
        await using var app = App(_factory, new());
        var ana = await CreateUserAsync(app, RoleNames.Student, "ana");
        var client = await SignedInClientAsync(app, ana);

        await SetLanguageAsync(client, "pt-BR");
        (await UserAsync(app, ana)).Language.Should().Be("pt-BR");

        await SetLanguageAsync(client, "xx-XX");
        (await UserAsync(app, ana)).Language.Should().Be("pt-BR", "the site doesn't have that language");

        await SetLanguageAsync(client, "FR-ca");
        (await UserAsync(app, ana)).Language.Should().Be("fr-CA");
    }

    [Fact]
    public async Task SigningUp_KeepsTheLanguageOfThePage()
    {
        await using var app = App(_factory, new());
        await EnsureRoleAsync(app, RoleNames.Student);
        var client = Client(app);
        var email = $"newcomer-{Guid.NewGuid():N}@example.test";
        var form = await PageAsync(client, "/Identity/Account/Register?culture=fr-CA");

        var response = await client.PostAsync("/Identity/Account/Register?culture=fr-CA", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FirstName"] = "New",
            ["Input.LastName"] = "Comer",
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
            ["__RequestVerificationToken"] = Token(form),
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "the account is created");
        (await DbAsync(app, db => db.Set<ApplicationUser>().AsNoTracking().SingleAsync(u => u.Email == email))).Language.Should().Be("fr-CA");
    }

    // The fixture's app with these settings on top, sending through a fake so no test ever reaches
    // a real mail server, on the given clock if any.
    private static WebApplicationFactory<Program> App(WebApplicationFactory<Program> factory, Dictionary<string, string?> settings,
        RecordingEmailSender? sender = null, TimeProvider? clock = null, Action<IServiceCollection>? services = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(s =>
            {
                var fake = sender ?? new RecordingEmailSender();
                s.RemoveAll<IEmailMessageSender>();
                s.RemoveAll<IEmailSender>();
                s.AddSingleton<IEmailMessageSender>(fake);
                s.AddSingleton<IEmailSender>(fake);
                if (clock is not null)
                {
                    s.RemoveAll<TimeProvider>();
                    s.AddSingleton(clock);
                }
                services?.Invoke(s);
            });
        });

    // Fails to save the day's count of e-mails, as when the database is unreachable.
    private sealed class FailingDailyCount : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => eventData.Context!.ChangeTracker.Entries<EmailDailyCount>().Any()
                ? throw new InvalidOperationException("The database is unreachable.")
                : ValueTask.FromResult(result);
    }

    private static string Limit(int skipped, int students) =>
        $"Daily email limit reached. Students who won't get an email about this routine: {skipped} of {students}. They'll still find it in My Training.";

    private static string[] Emails(params ApplicationUser[] users) => users.Select(u => u.Email!).ToArray();

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string DetailsUrl(int routineId) => $"/Teacher/Routines/Details/{routineId}";

    // The e-mails sent once the background jobs queued so far are done.
    private static async Task<List<(string To, EmailMessage Message)>> SentAsync(WebApplicationFactory<Program> app, RecordingEmailSender sender)
    {
        var queue = app.Services.GetRequiredService<BackgroundEmailQueue>();
        var waited = Stopwatch.StartNew();
        while (queue.Pending > 0)
        {
            waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the e-mails go out in the background");
            await Task.Delay(20);
        }
        return sender.Sent.ToList();
    }

    // Runs an e-mail job as the background worker does, in a scope of its own.
    private static async Task SendAsync(WebApplicationFactory<Program> app, RoutineEmailJob job)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoutineEmails>().SendAsync(job, CancellationToken.None);
    }

    // The language of an e-mail's HTML.
    private static string Lang(EmailMessage message) => Regex.Match(message.HtmlBody, "<html lang=\"([^\"]+)\"").Groups[1].Value;

    // Posts the assign form as a browser does: the ticked students go only when the teacher chooses.
    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, int routineId, int? classroomId,
        string recipients = "class", string[]? students = null, string? dueAt = null, string query = "")
    {
        var fields = new List<(string, string)>
        {
            ("RoutineId", Id(routineId)),
            ("ClassroomId", classroomId is int id ? Id(id) : ""),
            ("Recipients", recipients),
        };
        fields.AddRange((students ?? []).Select(s => ("StudentIds", s)));
        fields.Add(("DueAt", dueAt ?? ""));
        if (dueAt is not null) fields.Add(("AllowLate", "true"));
        fields.Add(("AllowLate", "false"));
        return PostAsync(client, "/Teacher/Routines/Assign" + query, [.. fields]);
    }

    private static async Task SetLanguageAsync(HttpClient client, string culture)
    {
        var response = await PostAsync(client, "/Dashboard/SetLanguage", ("culture", culture), ("returnUrl", "/"));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private sealed record StudentList(bool Shown, List<(string Id, bool Ticked)> Students, string Html);

    // The students the assign form lists for each classroom; the shown list is the one it posts.
    private static Dictionary<int, StudentList> StudentLists(string html) =>
        Regex.Matches(html, "<fieldset class=\"mb-3 ms-4\" data-aa-students=\"(\\d+)\"([^>]*)>(.*?)</fieldset>", RegexOptions.Singleline)
            .ToDictionary(
                m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                m => new StudentList(
                    Attribute(m.Groups[2].Value, "hidden") is null && Attribute(m.Groups[2].Value, "disabled") is null,
                    Regex.Matches(m.Groups[3].Value, "<input([^>]*)>")
                        .Select(i => i.Groups[1].Value)
                        .Where(a => Attribute(a, "name") == "StudentIds")
                        .Select(a => (Attribute(a, "value")!, Attribute(a, "checked") is not null))
                        .ToList(),
                    m.Groups[3].Value));

    // The values of the classroom select's options, in order.
    private static List<string> ClassroomOptions(string html)
    {
        var select = Regex.Match(html, "<select[^>]*\\sid=\"ClassroomId\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
        select.Success.Should().BeTrue("the form has a classroom select");
        return Regex.Matches(select.Groups[1].Value, "<option([^>]*)>").Select(o => Attribute(o.Groups[1].Value, "value") ?? "").ToList();
    }

    private static bool Checked(string html, string id)
    {
        var input = Regex.Match(html, $"<input([^>]*\\sid=\"{Regex.Escape(id)}\"[^>]*)>");
        input.Success.Should().BeTrue($"the page has the input {id}");
        return Attribute(input.Groups[1].Value, "checked") is not null;
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"\\s{name}=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static void ShowsSuccess(string page, string message)
        => page.Should().MatchRegex($"class=\"alert alert-success[^\"]*\" role=\"status\"[^>]*>\\s*<i [^>]*></i>\\s*<div>{Regex.Escape(message)}</div>");

    private static void ShowsError(string page, string message)
        => page.Should().MatchRegex($"class=\"alert alert-danger[^\"]*\" role=\"alert\">\\s*<i [^>]*></i>\\s*<div>{Regex.Escape(message)}</div>");

    // Expects a redirect to path, and returns the page there.
    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(path);
        return await PageTextAsync(client, path);
    }

    // Posts a form, with an antiforgery token from another page.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, params (string Name, string Value)[] fields)
    {
        var token = Token(await PageAsync(client, "/Home/Privacy"));
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> PageTextAsync(HttpClient client, string url)
        => WebUtility.HtmlDecode(await PageAsync(client, url));

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> app, ApplicationUser user)
    {
        var client = Client(app);
        var login = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "the account signs in");
        return client;
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

    // A user who can sign in, named after the given name so that names sort as given.
    private static async Task<ApplicationUser> CreateUserAsync(WebApplicationFactory<Program> app, string role, string name,
        string? language = null, bool confirmed = true, bool emailsOff = false)
    {
        await EnsureRoleAsync(app, role);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            FirstName = char.ToUpperInvariant(name[0]) + name[1..],
            LastName = "Tester",
            Language = language,
            RoutineEmailsOff = emailsOff,
        };
        var created = await users.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    // A routine of one exercise, so it can be assigned.
    private static Task<int> RoutineAsync(WebApplicationFactory<Program> app, ApplicationUser teacher, string name = "Notes") => DbAsync(app, async db =>
    {
        if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
        var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
        var routine = new Routine
        {
            Name = name,
            OwnerId = teacher.Id,
            Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 3 } }
        };
        db.Routines.Add(routine);
        await db.SaveChangesAsync();
        return routine.Id;
    });

    private static Task<int> ClassroomAsync(WebApplicationFactory<Program> app, ApplicationUser teacher, string name,
        ApplicationUser[]? members = null, bool archived = false) => DbAsync(app, async db =>
    {
        var classroom = new Classroom { Name = name, OwnerId = teacher.Id, IsArchived = archived };
        foreach (var member in members ?? [])
        {
            classroom.Members.Add(new ClassroomMember { StudentId = member.Id });
        }
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        return classroom.Id;
    });

    private static Task<int> JoinAsync(WebApplicationFactory<Program> app, int classroomId, ApplicationUser student) => DbAsync(app, db =>
    {
        db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = classroomId, StudentId = student.Id });
        return db.SaveChangesAsync();
    });

    // Assigns the routine to the classroom, to the chosen students only if any are given.
    private static Task<int> AssignInDbAsync(WebApplicationFactory<Program> app, int routineId, int classroomId,
        ApplicationUser[]? chosen = null) => DbAsync(app, async db =>
    {
        var assignment = new RoutineAssignment
        {
            RoutineId = routineId,
            ClassroomId = classroomId,
            ChosenStudentsOnly = chosen is not null,
            ChosenStudents = (chosen ?? []).Select(s => new RoutineAssignmentStudent { StudentId = s.Id }).ToList(),
        };
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    });

    private static Task<int> AssignmentCountAsync(WebApplicationFactory<Program> app, int routineId) =>
        DbAsync(app, db => db.RoutineAssignments.CountAsync(a => a.RoutineId == routineId));

    private static Task<ApplicationUser> UserAsync(WebApplicationFactory<Program> app, ApplicationUser user) =>
        DbAsync(app, db => db.Set<ApplicationUser>().AsNoTracking().SingleAsync(u => u.Id == user.Id));

    // The names of the routines the student finds in My Training.
    private static async Task<List<string>> RoutinesOfAsync(WebApplicationFactory<Program> app, ApplicationUser student)
    {
        using var scope = app.Services.CreateScope();
        var routines = await scope.ServiceProvider.GetRequiredService<RoutineRounds>().ListAsync(student.Id, TimeZoneInfo.Utc);
        return routines.Select(r => r.RoutineName).ToList();
    }

    private static async Task<T> DbAsync<T>(WebApplicationFactory<Program> app, Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = app.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
