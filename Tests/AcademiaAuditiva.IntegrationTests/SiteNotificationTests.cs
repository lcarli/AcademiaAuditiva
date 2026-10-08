using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The bell in the header and the Notifications page it opens (#109). Students are told when a
/// routine is assigned to them, and the day before it is due unless they finished it; teachers,
/// when a student finishes one of their routines or joins one of their classrooms. Each is told
/// once, nobody of what they did themself, and what the user did stands should that fail.
/// </summary>
public class SiteNotificationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Notify-Site!Pass1";
    private const string Assigned = "Routine assigned.";

    private static readonly Uri BaseAddress = new("http://localhost");

    // The page's texts in each language, without the markup around the names.
    private static readonly Dictionary<string, Texts> Translations = new()
    {
        ["en-US"] = new(
            Bell: "Notifications", BellUnread: "Notifications ({0} unread)", MarkAllRead: "Mark all as read",
            EmailSettings: "Email settings", Empty: "You have no notifications.", Unread: "Unread.", Due: "Due on {0}",
            Classroom: "Classroom: {0}", RoutineAssigned: "{0} assigned you the routine {1}.",
            RoutineDueTomorrow: "Reminder: the routine {0} is due on {1}.", StudentFinished: "{0} finished the routine {1}.",
            InviteAccepted: "{0} joined your classroom {1}."),
        ["pt-BR"] = new(
            Bell: "Notificações", BellUnread: "Notificações (não lidas: {0})", MarkAllRead: "Marcar todas como lidas",
            EmailSettings: "Configurar emails", Empty: "Você não tem notificações.", Unread: "Não lida.", Due: "Prazo até {0}",
            Classroom: "Turma: {0}", RoutineAssigned: "{0} atribuiu a você a rotina {1}.",
            RoutineDueTomorrow: "Lembrete: o prazo da rotina {0} vai até {1}.", StudentFinished: "{0} concluiu a rotina {1}.",
            InviteAccepted: "{0} entrou na sua turma {1}."),
        ["fr-CA"] = new(
            Bell: "Notifications", BellUnread: "Notifications (non lues\u00A0: {0})", MarkAllRead: "Tout marquer comme lu",
            EmailSettings: "Paramètres des courriels", Empty: "Vous n\u2019avez aucune notification.", Unread: "Non lue.",
            Due: "\u00C0 faire d\u2019ici le {0}", Classroom: "Classe\u00A0: {0}", RoutineAssigned: "{0} vous a assigné la routine {1}.",
            RoutineDueTomorrow: "Rappel\u00A0: la routine {0} est à faire d\u2019ici le {1}.", StudentFinished: "{0} a terminé la routine {1}.",
            InviteAccepted: "{0} a rejoint votre classe {1}."),
    };

    private readonly TestWebApplicationFactory _factory;

    public SiteNotificationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AssigningARoutine_TellsEachOfItsStudentsOnce_ButNotTheTeacher()
    {
        var now = new DateTimeOffset(2031, 5, 2, 9, 30, 0, TimeSpan.Zero);
        await using var app = App(new AnswerTimeTests.ManualClock(now));
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "assign-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "assign-ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "assign-bruno");
        var carla = await CreateUserAsync(app, RoleNames.Student, "assign-carla");
        var classroomId = await ClassroomAsync(app, teacher, "Assign class", [ana, bruno, carla, teacher]);
        var routine = await RoutineAsync(app, teacher, "Assigned scales");
        var client = await SignedInClientAsync(app, teacher);

        ShowsSuccess(await FollowAsync(client, await AssignAsync(client, routine.Id, classroomId, dueAt: "2031-05-09"), DetailsUrl(routine.Id)), Assigned);
        var toClass = await LatestAssignmentAsync(app, routine.Id);
        (await RowsAsync(app, n => n.RoutineAssignmentId == toClass)).Should().BeEquivalentTo(
            [Told(ana, toClass, now), Told(bruno, toClass, now), Told(carla, toClass, now)]);

        await FollowAsync(client, await AssignAsync(client, routine.Id, classroomId, "chosen", [bruno.Id]), DetailsUrl(routine.Id));
        var toBruno = await LatestAssignmentAsync(app, routine.Id);
        (await RowsAsync(app, n => n.RoutineAssignmentId == toBruno)).Should().Equal(Told(bruno, toBruno, now));

        await NotifyAsync(app, n => n.RoutineAssignedAsync(toClass));
        (await RowsAsync(app, n => n.RoutineAssignmentId == toClass)).Should().HaveCount(3, "each student is told once");

        // Older assignments went to one student.
        var toCarla = await AssignInDbAsync(app, routine.Id, now.UtcDateTime, student: carla);
        var toTeacher = await AssignInDbAsync(app, routine.Id, now.UtcDateTime, student: teacher);
        await NotifyAsync(app, n => n.RoutineAssignedAsync(toCarla));
        await NotifyAsync(app, n => n.RoutineAssignedAsync(toTeacher));
        await NotifyAsync(app, n => n.RoutineAssignedAsync(int.MaxValue));
        (await RowsAsync(app, n => n.RoutineAssignmentId == toCarla)).Should().Equal(Told(carla, toCarla, now));
        (await RowsAsync(app, n => n.RoutineAssignmentId == toTeacher || n.RoutineAssignmentId == int.MaxValue)).Should().BeEmpty();
        (await RowsAsync(app, n => n.UserId == teacher.Id || n.StudentId == teacher.Id)).Should().BeEmpty("nobody is told of what they did themself");
    }

    [Fact]
    public async Task NotificationsThatFailToSave_LeaveWhatTheUserDidDone()
    {
        await using var app = App(TimeProvider.System,
            s => s.ConfigureDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(new FailingNotifications())));
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "failing-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "failing-ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "failing-bruno");
        var classroomId = await ClassroomAsync(app, teacher, "Failing class", [ana]);
        var routine = await RoutineAsync(app, teacher, "Failing routine");

        var teacherClient = await SignedInClientAsync(app, teacher);
        ShowsSuccess(await FollowAsync(teacherClient, await AssignAsync(teacherClient, routine.Id, classroomId), DetailsUrl(routine.Id)), Assigned);
        var assignmentId = await LatestAssignmentAsync(app, routine.Id);

        await AcceptAsync(await SignedInClientAsync(app, bruno), await InviteAsync(app, classroomId, bruno));
        (await DbAsync(app, db => db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classroomId && m.StudentId == bruno.Id)))
            .Should().BeTrue("bruno joined the class all the same");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
            await notifier.RoutineAssignedAsync(assignmentId);
            await notifier.InviteAcceptedAsync(classroomId, teacher.Id, ana.Id);
            db.ChangeTracker.Entries<Notification>().Should().BeEmpty("the notifications that failed to save are dropped");

            db.Classrooms.Add(new Classroom { Name = "Saved afterwards", OwnerId = teacher.Id });
            await db.SaveChangesAsync();
        }

        (await DbAsync(app, db => db.Classrooms.CountAsync(c => c.Name == "Saved afterwards" && c.OwnerId == teacher.Id))).Should().Be(1);
        (await RowsAsync(app, n => n.RoutineAssignmentId == assignmentId || n.ClassroomId == classroomId)).Should().BeEmpty();
    }

    [Fact]
    public async Task StudentWhoJoinsAClassroom_IsAnnouncedToTheTeacherOnce()
    {
        var now = new DateTimeOffset(2031, 5, 3, 10, 0, 0, TimeSpan.Zero);
        await using var app = App(new AnswerTimeTests.ManualClock(now));
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "invite-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "invite-ana");
        var classroomId = await ClassroomAsync(app, teacher, "Invite class");
        var anaClient = await SignedInClientAsync(app, ana);

        var token = await InviteAsync(app, classroomId, ana);
        await AcceptAsync(anaClient, token);
        (await RowsAsync(app, n => n.ClassroomId == classroomId)).Should().Equal(
            new Row(teacher.Id, NotificationKind.InviteAccepted, ana.Id, null, classroomId, now.UtcDateTime, null));

        (await anaClient.GetAsync($"/invite/accept?token={token}")).StatusCode.Should().Be(HttpStatusCode.OK, "the invitation was used");
        await AcceptAsync(anaClient, await InviteAsync(app, classroomId, ana));
        await AcceptAsync(await SignedInClientAsync(app, teacher), await InviteAsync(app, classroomId, teacher));

        (await DbAsync(app, db => db.ClassroomMembers.CountAsync(m => m.ClassroomId == classroomId))).Should().Be(2, "the teacher joined too");
        (await RowsAsync(app, n => n.ClassroomId == classroomId)).Should().ContainSingle(
            "ana was a member already the second time, and the teacher isn't told of joining their own class");
    }

    [Fact]
    public async Task StudentWhoFinishesARoutine_IsAnnouncedToItsTeacherOnce()
    {
        var now = new DateTimeOffset(2031, 5, 4, 10, 0, 0, TimeSpan.Zero);
        await using var app = App(new AnswerTimeTests.ManualClock(now));
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "finish-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "finish-ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "finish-bruno");
        var classroomId = await ClassroomAsync(app, teacher, "Finish class", [ana, teacher]);
        var routine = await RoutineAsync(app, teacher, "Two exercises", items: 2);
        var assignmentId = await AssignInDbAsync(app, routine.Id, now.UtcDateTime.AddDays(-1), classroomId);

        await AnswerAsync(app, ana, assignmentId, routine, item: 0);
        await NotifyAsync(app, n => n.RoutineItemCompletedAsync(ana.Id, assignmentId));
        (await RowsAsync(app, n => n.RoutineAssignmentId == assignmentId)).Should().BeEmpty("the routine's other exercise is left");

        await AnswerAsync(app, ana, assignmentId, routine, item: 1);
        await NotifyAsync(app, n => n.RoutineItemCompletedAsync(ana.Id, assignmentId));
        await NotifyAsync(app, n => n.RoutineItemCompletedAsync(ana.Id, assignmentId));
        (await RowsAsync(app, n => n.RoutineAssignmentId == assignmentId)).Should().Equal(
            new Row(teacher.Id, NotificationKind.StudentFinishedRoutine, ana.Id, assignmentId, null, now.UtcDateTime, null));

        foreach (var other in new[] { teacher, bruno })
        {
            await AnswerAsync(app, other, assignmentId, routine, item: 0);
            await AnswerAsync(app, other, assignmentId, routine, item: 1);
            await NotifyAsync(app, n => n.RoutineItemCompletedAsync(other.Id, assignmentId));
        }
        (await RowsAsync(app, n => n.RoutineAssignmentId == assignmentId)).Should().ContainSingle(
            "the teacher took their own routine, and it isn't bruno's");
    }

    [Fact]
    public async Task StudentsAreRemindedOnce_FromNoonUtc_OfTheRoutinesDueTomorrowThatTheyHaveNotFinished()
    {
        // No other test of this class has a routine due on 11 June 2031: the reminders look at every routine.
        var clock = new AnswerTimeTests.ManualClock(new DateTimeOffset(2031, 6, 10, 11, 0, 0, TimeSpan.Zero));
        await using var app = App(clock);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "remind-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "remind-ana");
        var bruno = await CreateUserAsync(app, RoleNames.Student, "remind-bruno");
        var carla = await CreateUserAsync(app, RoleNames.Student, "remind-carla");
        var dora = await CreateUserAsync(app, RoleNames.Student, "remind-dora");
        var classroomId = await ClassroomAsync(app, teacher, "Remind class", [ana, bruno, teacher]);
        var archivedId = await ClassroomAsync(app, teacher, "Archived class", [ana], archived: true);
        var routine = await RoutineAsync(app, teacher, "Due soon");
        var tomorrow = new DateTime(2031, 6, 11);
        var before = new DateTime(2031, 6, 8, 9, 0, 0);

        var toClass = await AssignInDbAsync(app, routine.Id, before, classroomId, tomorrow);
        var toBruno = await AssignInDbAsync(app, routine.Id, before, classroomId, tomorrow, chosen: [bruno]);
        var toCarla = await AssignInDbAsync(app, routine.Id, before, dueAt: tomorrow, student: carla);
        // Left out: assigned today, due today, the day after or never, in an archived classroom, and with nothing left to do.
        await AssignInDbAsync(app, routine.Id, new DateTime(2031, 6, 10, 8, 0, 0), classroomId, tomorrow);
        await AssignInDbAsync(app, routine.Id, before, classroomId, new DateTime(2031, 6, 10));
        await AssignInDbAsync(app, routine.Id, before, classroomId, new DateTime(2031, 6, 12));
        await AssignInDbAsync(app, routine.Id, before, classroomId);
        await AssignInDbAsync(app, routine.Id, before, archivedId, tomorrow);
        var nothingLeft = await AssignInDbAsync(app, routine.Id, before, classroomId, tomorrow, chosen: [ana]);
        await ExcludeAsync(app, nothingLeft, ana, routine.ItemIds[0]);
        await AnswerAsync(app, bruno, toClass, routine, item: 0);
        var people = new[] { teacher.Id, ana.Id, bruno.Id, carla.Id, dora.Id };
        Expression<Func<Notification, bool>> reminders = n => n.Kind == NotificationKind.RoutineDueTomorrow && people.Contains(n.UserId);

        (await RemindAsync(app)).Should().Be(0, "students are reminded from noon UTC");
        (await RowsAsync(app, reminders)).Should().BeEmpty();

        clock.Advance(TimeSpan.FromHours(1));
        var noon = new DateTime(2031, 6, 10, 12, 0, 0);
        (await RemindAsync(app)).Should().Be(3);
        (await RowsAsync(app, reminders)).Should().BeEquivalentTo(
            [Reminded(ana, toClass, noon), Reminded(bruno, toBruno, noon), Reminded(carla, toCarla, noon)],
            "bruno finished the routine of the whole class, and the teacher took it too");
        (await RemindAsync(app)).Should().Be(0, "each student is reminded once");

        await JoinAsync(app, classroomId, dora);
        clock.Advance(TimeSpan.FromHours(1));
        (await RemindAsync(app)).Should().Be(1, "who joins the class later that day is reminded too");
        (await RowsAsync(app, n => n.UserId == dora.Id)).Should().Equal(Reminded(dora, toClass, noon.AddHours(1)));
    }

    [Fact]
    public async Task Bell_ShowsHowManyNotificationsAreUnread_UpToNine()
    {
        var teacher = await CreateUserAsync(_factory, RoleNames.Teacher, "bell-teacher");
        var ana = await CreateUserAsync(_factory, RoleNames.Student, "bell-ana");
        var classroomId = await ClassroomAsync(_factory, teacher, "Bell class", [ana]);
        var at = new DateTime(2031, 4, 1, 12, 0, 0);
        var client = await SignedInClientAsync(_factory, teacher);
        Task AddAsync(int unread) => DbAsync(_factory, db =>
        {
            for (var i = 0; i < unread; i++)
            {
                db.Notifications.Add(Joined(teacher, ana, classroomId, at));
            }
            return db.SaveChangesAsync();
        });

        Bell(await PageAsync(client, "/Home/Privacy")).Should().Be(new BellView("bi-bell", null, "Notifications", null));

        await AddAsync(unread: 3);
        await AddNotificationAsync(_factory, teacher, NotificationKind.InviteAccepted, ana, at, classroomId: classroomId, readAt: at);
        Bell(await PageAsync(client, "/Home/Privacy")).Should().Be(new BellView("bi-bell-fill", "3", "Notifications (3 unread)", null));

        await AddAsync(unread: 6);
        Bell(await PageAsync(client, "/Home/Privacy")).Should().Be(new BellView("bi-bell-fill", "9", "Notifications (9 unread)", null));

        await AddAsync(unread: 1);
        Bell(await PageAsync(client, "/Home/Privacy")).Should().Be(new BellView("bi-bell-fill", "9+", "Notifications (9+ unread)", null));

        Bell(await PageAsync(client, "/Notifications")).AriaCurrent.Should().Be("page");
        Bell(await PageAsync(client, "/Identity/Account/Manage/Notifications")).AriaCurrent.Should().BeNull("that page holds the e-mail settings");
        (await PageAsync(Anonymous(_factory), "/Home/Privacy")).Should().NotContain("notificationBell");
    }

    [Theory]
    [InlineData("en-US", null, 0)]
    [InlineData("pt-BR", "America/Sao_Paulo", -3)]
    [InlineData("fr-CA", "America/Toronto", -4)]
    public async Task Page_ListsTheNotificationsNewestFirst_InTheReadersLanguageAndTimeZone(string culture, string? timeZone, int utcOffset)
    {
        const string RoutineName = "Thirds & <Fifths>";
        const string ClassroomName = "Choir 5e";
        var texts = Translations[culture];
        var format = new CultureInfo(culture);
        string F(string text, params object[] args) => string.Format(format, text, args);
        string Local(DateTime utc) => utc.AddHours(utcOffset).ToString("g", format);

        var teacher = await CreateUserAsync(_factory, RoleNames.Teacher, "page-teacher");
        var ana = await CreateUserAsync(_factory, RoleNames.Student, "page-ana");
        var rita = await CreateUserAsync(_factory, RoleNames.Student, "page-rita", userName: $"rita-{Guid.NewGuid():N}");
        var classroomId = await ClassroomAsync(_factory, teacher, ClassroomName, [ana, rita]);
        var routine = await RoutineAsync(_factory, teacher, RoutineName);
        var solo = await RoutineAsync(_factory, teacher, "Solo");
        var dueAt = new DateTime(2031, 7, 4);
        var due = dueAt.ToString("D", format);
        var toClass = await AssignInDbAsync(_factory, routine.Id, new DateTime(2031, 6, 30, 14, 0, 0), classroomId, dueAt);
        var toAna = await AssignInDbAsync(_factory, solo.Id, new DateTime(2031, 6, 29, 7, 0, 0), student: ana);

        var soloAt = new DateTime(2031, 6, 29, 8, 0, 0);
        var assignedAt = new DateTime(2031, 6, 30, 14, 5, 0);
        var remindedAt = new DateTime(2031, 7, 3, 12, 0, 0);
        var finishedAt = new DateTime(2031, 7, 1, 16, 45, 0);
        var joinedAt = new DateTime(2031, 7, 3, 1, 30, 0);
        var soloAssigned = await AddNotificationAsync(_factory, ana, NotificationKind.RoutineAssigned, ana, soloAt, toAna);
        var assigned = await AddNotificationAsync(_factory, ana, NotificationKind.RoutineAssigned, ana, assignedAt, toClass);
        var reminded = await AddNotificationAsync(_factory, ana, NotificationKind.RoutineDueTomorrow, ana, remindedAt, toClass,
            readAt: remindedAt.AddMinutes(30));
        var finished = await AddNotificationAsync(_factory, teacher, NotificationKind.StudentFinishedRoutine, ana, finishedAt, toClass,
            readAt: finishedAt.AddHours(1));
        var joined = await AddNotificationAsync(_factory, teacher, NotificationKind.InviteAccepted, rita, joinedAt, classroomId: classroomId);

        var studentPage = await PageAsync(await SignedInClientAsync(_factory, ana, timeZone), $"/Notifications?culture={culture}");
        Items(studentPage).Should().Equal(
            new Item(Open(reminded), Unread: false, Dot: false, "2031-07-03T12:00:00.0000000Z",
                $"{F(texts.RoutineDueTomorrow, RoutineName, due)} {F(texts.Classroom, ClassroomName)} · {Local(remindedAt)}"),
            new Item(Open(assigned), Unread: true, Dot: true, "2031-06-30T14:05:00.0000000Z",
                $"{texts.Unread} {F(texts.RoutineAssigned, teacher.UserName!, RoutineName)} {F(texts.Classroom, ClassroomName)} · {F(texts.Due, due)} · {Local(assignedAt)}"),
            new Item(Open(soloAssigned), Unread: true, Dot: true, "2031-06-29T08:00:00.0000000Z",
                $"{texts.Unread} {F(texts.RoutineAssigned, teacher.UserName!, "Solo")} {Local(soloAt)}"));
        Bell(studentPage).Should().Be(new BellView("bi-bell-fill", "2", F(texts.BellUnread, 2), "page"));
        Text(studentPage).Should().Contain(texts.MarkAllRead);

        var teacherPage = await PageAsync(await SignedInClientAsync(_factory, teacher, timeZone), $"/Notifications?culture={culture}");
        Items(teacherPage).Should().Equal(
            new Item(Open(joined), Unread: true, Dot: true, "2031-07-03T01:30:00.0000000Z",
                $"{texts.Unread} {F(texts.InviteAccepted, $"{rita.UserName} ({rita.Email})", ClassroomName)} {Local(joinedAt)}"),
            new Item(Open(finished), Unread: false, Dot: false, "2031-07-01T16:45:00.0000000Z",
                $"{F(texts.StudentFinished, ana.UserName!, RoutineName)} {F(texts.Classroom, ClassroomName)} · {Local(finishedAt)}"));
        Bell(teacherPage).Label.Should().Be(F(texts.BellUnread, 1));

        foreach (var page in new[] { studentPage, teacherPage })
        {
            page.Should().Contain("Thirds &amp; &lt;Fifths&gt;").And.NotContain("<Fifths>", "names are encoded");
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public async Task Page_WithoutNotifications_SaysSo_AndLinksToTheEmailSettings(string culture)
    {
        var texts = Translations[culture];
        var user = await CreateUserAsync(_factory, RoleNames.Student, "empty");

        var page = await PageAsync(await SignedInClientAsync(_factory, user), $"/Notifications?culture={culture}");

        Text(page).Should().Contain(texts.Empty);
        var settings = Regex.Match(page, "<a class=\"btn btn-sm aa-ghost-btn\"([^>]*)>(.*?)</a>", RegexOptions.Singleline);
        settings.Success.Should().BeTrue("the page links to the e-mail settings");
        Attribute(settings.Groups[1].Value, "href").Should().Be("/Identity/Account/Manage/Notifications");
        Text(settings.Groups[2].Value).Should().Be(texts.EmailSettings);
        page.Should().NotContain("/Notifications/MarkAllRead", "there is nothing to mark read");
        Items(page).Should().BeEmpty();
        Bell(page).Should().Be(new BellView("bi-bell", null, texts.Bell, "page"));
    }

    [Fact]
    public async Task Page_ListsTheNewestHundredNotifications()
    {
        var teacher = await CreateUserAsync(_factory, RoleNames.Teacher, "many-teacher");
        var ana = await CreateUserAsync(_factory, RoleNames.Student, "many-ana");
        var classroomId = await ClassroomAsync(_factory, teacher, "Many class", [ana]);
        var first = new DateTime(2031, 3, 1, 8, 0, 0);
        var ids = await DbAsync(_factory, async db =>
        {
            var notifications = Enumerable.Range(0, NotificationInbox.MostListed + 1)
                .Select(i => Joined(teacher, ana, classroomId, first.AddMinutes(i)))
                .ToList();
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();
            return notifications.Select(n => n.Id).ToList();
        });

        var listed = Items(await PageAsync(await SignedInClientAsync(_factory, teacher), "/Notifications"));

        listed.Select(i => i.Href).Should().Equal(ids.AsEnumerable().Reverse().Take(NotificationInbox.MostListed).Select(Open));
    }

    [Fact]
    public async Task OpeningANotification_MarksItRead_AndGoesToWhatItIsAbout()
    {
        var clock = new AnswerTimeTests.ManualClock(new DateTimeOffset(2031, 8, 1, 10, 0, 0, TimeSpan.Zero));
        await using var app = App(clock);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "open-teacher");
        var ana = await CreateUserAsync(app, RoleNames.Student, "open-ana");
        var classroomId = await ClassroomAsync(app, teacher, "Open class", [ana]);
        var routine = await RoutineAsync(app, teacher, "Open routine");
        var at = new DateTime(2031, 7, 30, 9, 0, 0);
        var assignmentId = await AssignInDbAsync(app, routine.Id, at, classroomId);
        var assigned = await AddNotificationAsync(app, ana, NotificationKind.RoutineAssigned, ana, at, assignmentId);
        var reminded = await AddNotificationAsync(app, ana, NotificationKind.RoutineDueTomorrow, ana, at, assignmentId);
        var finished = await AddNotificationAsync(app, teacher, NotificationKind.StudentFinishedRoutine, ana, at, assignmentId);
        var joined = await AddNotificationAsync(app, teacher, NotificationKind.InviteAccepted, ana, at, classroomId: classroomId);
        var anaClient = await SignedInClientAsync(app, ana);
        var teacherClient = await SignedInClientAsync(app, teacher);

        (await anaClient.GetAsync(Open(finished))).StatusCode.Should().Be(HttpStatusCode.NotFound, "it is the teacher's");
        (await anaClient.GetAsync(Open(int.MaxValue))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAtAsync(app, finished)).Should().BeNull();

        var myTraining = $"/MyTraining#routine-{assignmentId}";
        await OpensAsync(anaClient, assigned, myTraining);
        await OpensAsync(anaClient, reminded, myTraining);
        await OpensAsync(teacherClient, finished, $"/Teacher/Dashboard/Student/{ana.Id}?assignmentId={assignmentId}");
        await OpensAsync(teacherClient, joined, $"/Teacher/Classrooms/Details/{classroomId}");
        var openedAt = clock.GetUtcNow().UtcDateTime;
        foreach (var id in new[] { assigned, reminded, finished, joined })
        {
            (await ReadAtAsync(app, id)).Should().Be(openedAt);
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        await OpensAsync(anaClient, assigned, myTraining);
        (await ReadAtAsync(app, assigned)).Should().Be(openedAt, "it was read then");
    }

    [Fact]
    public async Task MarkingAllRead_ReadsTheUsersUnreadNotifications_Only()
    {
        var clock = new AnswerTimeTests.ManualClock(new DateTimeOffset(2031, 8, 2, 10, 0, 0, TimeSpan.Zero));
        await using var app = App(clock);
        var teacher = await CreateUserAsync(app, RoleNames.Teacher, "read-teacher");
        var colleague = await CreateUserAsync(app, RoleNames.Teacher, "read-colleague");
        var ana = await CreateUserAsync(app, RoleNames.Student, "read-ana");
        var classroomId = await ClassroomAsync(app, teacher, "Read class", [ana]);
        var at = new DateTime(2031, 8, 1, 9, 0, 0);
        var readEarlier = at.AddMinutes(30);
        var unread = await AddNotificationAsync(app, teacher, NotificationKind.InviteAccepted, ana, at, classroomId: classroomId);
        var read = await AddNotificationAsync(app, teacher, NotificationKind.InviteAccepted, ana, at, classroomId: classroomId, readAt: readEarlier);
        var colleagues = await AddNotificationAsync(app, colleague, NotificationKind.InviteAccepted, ana, at, classroomId: classroomId);
        var client = await SignedInClientAsync(app, teacher);

        (await client.PostAsync("/Notifications/MarkAllRead", new FormUrlEncodedContent([]))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the form sends an antiforgery token");
        (await ReadAtAsync(app, unread)).Should().BeNull();

        var page = await FollowAsync(client, await PostAsync(client, "/Notifications/MarkAllRead"), "/Notifications");

        ShowsSuccess(page, "All notifications marked as read.");
        page.Should().NotContain("/Notifications/MarkAllRead");
        Items(page).Select(i => (i.Href, i.Unread, i.Dot)).Should().BeEquivalentTo([(Open(unread), false, false), (Open(read), false, false)]);
        Bell(page).Should().Be(new BellView("bi-bell", null, "Notifications", "page"));
        (await ReadAtAsync(app, unread)).Should().Be(clock.GetUtcNow().UtcDateTime);
        (await ReadAtAsync(app, read)).Should().Be(readEarlier);
        (await ReadAtAsync(app, colleagues)).Should().BeNull();
    }

    [Theory]
    [InlineData("/Notifications", "/Identity/Account/Login?ReturnUrl=%2FNotifications")]
    [InlineData("/Notifications/Open/1", "/Identity/Account/Login?ReturnUrl=%2FNotifications%2FOpen%2F1")]
    public async Task Notifications_AreForSignedInUsers(string url, string login)
    {
        var response = await Anonymous(_factory).GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        new Uri(BaseAddress, response.Headers.Location!).PathAndQuery.Should().Be(login);
    }

    private sealed record Texts(string Bell, string BellUnread, string MarkAllRead, string EmailSettings, string Empty, string Unread,
        string Due, string Classroom, string RoutineAssigned, string RoutineDueTomorrow, string StudentFinished, string InviteAccepted);

    private sealed record Row(string UserId, NotificationKind Kind, string StudentId, int? AssignmentId, int? ClassroomId,
        DateTime CreatedAt, DateTime? ReadAt);

    private sealed record RoutineSeed(int Id, int ExerciseId, int Target, IReadOnlyList<int> ItemIds);

    private sealed record BellView(string Icon, string? Count, string Label, string? AriaCurrent);

    private sealed record Item(string Href, bool Unread, bool Dot, string Time, string Text);

    // The fixture's app on the given clock, sending through a fake so no test ever reaches a real
    // mail server (and none is set up, so routines are assigned without e-mails).
    private WebApplicationFactory<Program> App(TimeProvider clock, Action<IServiceCollection>? services = null) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(s =>
        {
            var sender = new RecordingEmailSender();
            s.RemoveAll<IEmailMessageSender>();
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailMessageSender>(sender);
            s.AddSingleton<IEmailSender>(sender);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton(clock);
            services?.Invoke(s);
        }));

    // Fails to save notifications, as when the database is unreachable.
    private sealed class FailingNotifications : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => eventData.Context!.ChangeTracker.Entries<Notification>().Any(e => e.State == EntityState.Added)
                ? throw new InvalidOperationException("The database is unreachable.")
                : ValueTask.FromResult(result);
    }

    private static Row Told(ApplicationUser student, int assignmentId, DateTimeOffset at) =>
        new(student.Id, NotificationKind.RoutineAssigned, student.Id, assignmentId, null, at.UtcDateTime, null);

    private static Row Reminded(ApplicationUser student, int assignmentId, DateTime at) =>
        new(student.Id, NotificationKind.RoutineDueTomorrow, student.Id, assignmentId, null, at, null);

    private static Notification Joined(ApplicationUser teacher, ApplicationUser student, int classroomId, DateTime at) => new()
    {
        UserId = teacher.Id,
        Kind = NotificationKind.InviteAccepted,
        StudentId = student.Id,
        ClassroomId = classroomId,
        CreatedAt = at,
    };

    private static string Open(int id) => $"/Notifications/Open/{id.ToString(CultureInfo.InvariantCulture)}";

    private static string DetailsUrl(int routineId) => $"/Teacher/Routines/Details/{routineId.ToString(CultureInfo.InvariantCulture)}";

    // The header's bell, which must open the notifications and be named the same to all readers.
    private static BellView Bell(string html)
    {
        var bells = Regex.Matches(html, "<a id=\"notificationBell\"([^>]*)>(.*?)</a>", RegexOptions.Singleline);
        bells.Should().ContainSingle("the header has a bell");
        var attributes = bells[0].Groups[1].Value;
        var content = bells[0].Groups[2].Value;
        Attribute(attributes, "href").Should().Be("/Notifications");
        var label = Attribute(attributes, "aria-label");
        label.Should().NotBeNullOrEmpty();
        Attribute(attributes, "title").Should().Be(label);
        var icon = Regex.Match(content, "<i class=\"bi (bi-bell(?:-fill)?)\" aria-hidden=\"true\"></i>");
        icon.Success.Should().BeTrue("the bell has an icon");
        var count = Regex.Match(content, "<span class=\"aa-bell-count\" aria-hidden=\"true\">([^<]*)</span>");
        return new BellView(icon.Groups[1].Value, count.Success ? WebUtility.HtmlDecode(count.Groups[1].Value) : null,
            label!, Attribute(attributes, "aria-current"));
    }

    // The notifications the page lists, in order.
    private static List<Item> Items(string html) =>
        Regex.Matches(html, "<a class=\"(list-group-item list-group-item-action[^\"]*)\"([^>]*)>(.*?)</a>", RegexOptions.Singleline)
            .Select(m => new Item(
                Attribute(m.Groups[2].Value, "href")!,
                Unread: m.Groups[1].Value.Split(' ').Contains("aa-notification-unread"),
                Dot: m.Groups[3].Value.Contains("<span class=\"aa-notification-dot\" aria-hidden=\"true\"></span>"),
                Regex.Match(m.Groups[3].Value, "<time datetime=\"([^\"]*)\">").Groups[1].Value,
                Text(m.Groups[3].Value)))
            .ToList();

    // Some HTML's text as read: without its tags, with each run of spaces and line breaks as one space.
    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "")), "[ \t\r\n]+", " ").Trim();

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"\\s{name}=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static void ShowsSuccess(string page, string message)
        => page.Should().MatchRegex($"class=\"alert alert-success[^\"]*\" role=\"status\"[^>]*>\\s*<i [^>]*></i>\\s*<div>{Regex.Escape(message)}</div>");

    private static async Task OpensAsync(HttpClient client, int id, string location)
    {
        var response = await client.GetAsync(Open(id));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(location);
        await PageAsync(client, location.Split('#')[0]);
    }

    // Expects a redirect to path, and returns the page there.
    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(path);
        return await PageAsync(client, path);
    }

    // Posts the assign form as a browser does: the ticked students go only when the teacher chooses.
    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, int routineId, int classroomId,
        string recipients = "class", string[]? students = null, string? dueAt = null)
    {
        var fields = new List<(string, string)>
        {
            ("RoutineId", routineId.ToString(CultureInfo.InvariantCulture)),
            ("ClassroomId", classroomId.ToString(CultureInfo.InvariantCulture)),
            ("Recipients", recipients),
        };
        fields.AddRange((students ?? []).Select(s => ("StudentIds", s)));
        fields.Add(("DueAt", dueAt ?? ""));
        fields.Add(("AllowLate", "false"));
        return PostAsync(client, "/Teacher/Routines/Assign", [.. fields]);
    }

    private static async Task AcceptAsync(HttpClient client, string token)
    {
        var response = await client.GetAsync($"/invite/accept?token={Uri.EscapeDataString(token)}");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Dashboard", "the invitation is accepted");
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

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    private static HttpClient Anonymous(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Signs the user in, in the browser's time zone if one is given.
    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> app, ApplicationUser user, string? timeZone = null)
    {
        var cookies = new CookieContainer();
        if (timeZone is not null)
        {
            cookies.Add(BaseAddress, new Cookie(UserTimeZone.CookieName, Uri.EscapeDataString(timeZone)));
        }
        var client = app.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));
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

    // A user who can sign in with their e-mail address, unless given another user name.
    private static async Task<ApplicationUser> CreateUserAsync(WebApplicationFactory<Program> app, string role, string name, string? userName = null)
    {
        await EnsureRoleAsync(app, role);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = userName ?? email,
            Email = email,
            EmailConfirmed = true,
            FirstName = name,
            LastName = "Tester",
        };
        var created = await users.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    // A routine of GuessNote exercises, each of the given number of questions.
    private static Task<RoutineSeed> RoutineAsync(WebApplicationFactory<Program> app, ApplicationUser teacher, string name,
        int items = 1, int target = 1) => DbAsync(app, async db =>
    {
        if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
        var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
        var routine = new Routine { Name = name, OwnerId = teacher.Id };
        for (var order = 1; order <= items; order++)
        {
            routine.Items.Add(new RoutineItem { ExerciseId = exerciseId, Order = order, TargetCount = target });
        }
        db.Routines.Add(routine);
        await db.SaveChangesAsync();
        return new RoutineSeed(routine.Id, exerciseId, target, routine.Items.OrderBy(i => i.Order).Select(i => i.Id).ToList());
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

    // Assigns the routine to the classroom (to the chosen students only, if any are given) or, as
    // older assignments were, to one student.
    private static Task<int> AssignInDbAsync(WebApplicationFactory<Program> app, int routineId, DateTime assignedAt,
        int? classroomId = null, DateTime? dueAt = null, ApplicationUser[]? chosen = null, ApplicationUser? student = null) => DbAsync(app, async db =>
    {
        var assignment = new RoutineAssignment
        {
            RoutineId = routineId,
            ClassroomId = classroomId,
            StudentId = student?.Id,
            AssignedAt = assignedAt,
            DueAt = dueAt,
            ChosenStudentsOnly = chosen is not null,
            ChosenStudents = (chosen ?? []).Select(s => new RoutineAssignmentStudent { StudentId = s.Id }).ToList(),
        };
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    });

    private static Task<int> LatestAssignmentAsync(WebApplicationFactory<Program> app, int routineId) =>
        DbAsync(app, db => db.RoutineAssignments.Where(a => a.RoutineId == routineId).MaxAsync(a => a.Id));

    // The teacher took this item out of the student's routine.
    private static Task<int> ExcludeAsync(WebApplicationFactory<Program> app, int assignmentId, ApplicationUser student, int itemId) => DbAsync(app, db =>
    {
        db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
        {
            RoutineAssignmentId = assignmentId,
            StudentId = student.Id,
            RoutineItemId = itemId,
            ExcludeItem = true,
        });
        return db.SaveChangesAsync();
    });

    // Answers every question of one of the routine's exercises, right or wrong.
    private static Task<int> AnswerAsync(WebApplicationFactory<Program> app, ApplicationUser student, int assignmentId,
        RoutineSeed routine, int item) => DbAsync(app, db =>
    {
        for (var question = 1; question <= routine.Target; question++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = student.Id,
                ExerciseId = routine.ExerciseId,
                IsCorrect = question % 2 == 1,
                Timestamp = new DateTime(2031, 1, 1),
                RoutineAssignmentId = assignmentId,
                RoutineItemId = routine.ItemIds[item],
                RoutineQuestion = question,
            });
        }
        return db.SaveChangesAsync();
    });

    private static Task<string> InviteAsync(WebApplicationFactory<Program> app, int classroomId, ApplicationUser invitee) => DbAsync(app, async db =>
    {
        var token = Guid.NewGuid().ToString("N");
        db.ClassroomInvites.Add(new ClassroomInvite
        {
            ClassroomId = classroomId,
            Email = invitee.Email!,
            Token = token,
            // Invitations expire by the real clock.
            ExpiresAt = DateTime.UtcNow.AddDays(14),
        });
        await db.SaveChangesAsync();
        return token;
    });

    private static Task<int> AddNotificationAsync(WebApplicationFactory<Program> app, ApplicationUser reader, NotificationKind kind,
        ApplicationUser student, DateTime createdAt, int? assignmentId = null, int? classroomId = null, DateTime? readAt = null) => DbAsync(app, async db =>
    {
        var notification = new Notification
        {
            UserId = reader.Id,
            Kind = kind,
            StudentId = student.Id,
            RoutineAssignmentId = assignmentId,
            ClassroomId = classroomId,
            CreatedAt = createdAt,
            ReadAt = readAt,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    });

    private static Task<List<Row>> RowsAsync(WebApplicationFactory<Program> app, Expression<Func<Notification, bool>> where) =>
        DbAsync(app, db => db.Notifications.AsNoTracking()
            .Where(where)
            .OrderBy(n => n.Id)
            .Select(n => new Row(n.UserId, n.Kind, n.StudentId, n.RoutineAssignmentId, n.ClassroomId, n.CreatedAt, n.ReadAt))
            .ToListAsync());

    private static Task<DateTime?> ReadAtAsync(WebApplicationFactory<Program> app, int id) =>
        DbAsync(app, db => db.Notifications.Where(n => n.Id == id).Select(n => n.ReadAt).SingleAsync());

    // Runs the notifier as a request or the hourly job does, in a scope of its own.
    private static async Task NotifyAsync(WebApplicationFactory<Program> app, Func<Notifier, Task> work)
    {
        using var scope = app.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<Notifier>());
    }

    private static async Task<int> RemindAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Notifier>().RemindDueTomorrowAsync();
    }

    private static async Task<T> DbAsync<T>(WebApplicationFactory<Program> app, Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = app.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
