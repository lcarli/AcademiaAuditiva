using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A teacher's reports (#107) count only the answers given in the routines they assigned: never
/// a student's practice outside them nor another teacher's routines, and only from students still
/// in one of their classes. A report on anything that isn't theirs is not found.
/// </summary>
public class TeacherReportTests : IClassFixture<TeacherReportTests.Factory>
{
    private const string Password = "Teacher-Report!Pass1";
    private const string NoStudents = "No student takes this routine right now.";
    private const string NoRoutines = "None of your routines is assigned to this student yet.";
    private const string Excluded = "Excluded for this student";

    private static readonly Uri BaseAddress = new("http://localhost");

    /// <summary>A Tuesday afternoon, where the app's clock stands still.</summary>
    private static readonly DateTime Now = new(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc);

    private readonly Factory _factory;

    public TeacherReportTests(Factory factory) => _factory = factory;

    // ----- What counts -----

    [Fact]
    public async Task Reports_CountOnlyAnswersGivenInTheTeachersRoutines()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var otherTeacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var scales = await RoutineAsync(teacher, "Scales", ("GuessNote", 4));
        var scalesId = await AssignAsync(scales, classroomId: choir);
        await AnswerAsync(scalesId, scales, 0, ana, Now.AddHours(-1), 6, true, false);
        // Later, the same student answers another teacher's routine and practises on her own.
        var band = await ClassroomAsync(otherTeacher, "Brass Band", ana);
        var fanfare = await RoutineAsync(otherTeacher, "Fanfare", ("GuessNote", 5));
        var fanfareId = await AssignAsync(fanfare, classroomId: band);
        await AnswerAsync(fanfareId, fanfare, 0, ana, Now.AddMinutes(-30), 9, true, true, true);
        await PracticeAsync(ana, "GuessNote", Now, right: 5, wrong: 0);

        var take = (await AssignmentReportAsync(teacher, scalesId))!.Takes.Should().ContainSingle().Subject;
        (take.Student.Id, take.Status, take.Attempts, take.Correct, take.LastAnswerAt)
            .Should().Be((ana.Id, TakeStatus.InProgress, 2, 1, (DateTime?)Now.AddHours(-1)));
        take.Items.Single()!.Seconds.Should().Be(12);
        (await AssignmentReportAsync(otherTeacher, fanfareId))!.Takes.Single().Attempts
            .Should().Be(3, "each teacher counts the answers to their own routine");

        var client = await SignedInClientAsync(teacher);
        var routinePage = await PageTextAsync(client, AssignmentUrl(scalesId));
        Stats(routinePage).Should().Equal(
            ("Students", "1"), ("Finished", "0"), ("In progress", "1"), ("Not started", "0"), ("Late", "0"), ("Accuracy", "50%"));
        Row(routinePage, "<div>1. Guess Note</div>").Should().Equal("1. Guess Note target: 4", "0/1", "2", "50%", "6 s");
        Row(routinePage, StudentLink(ana)).Should().Equal(ana.UserName, "In progress", "2/4 50%", "50%", "—");

        var classPage = await PageTextAsync(client, ClassroomUrl(choir));
        Stats(classPage).Should().Equal(("Students", "1"), ("Routines", "1"), ("Routines finished", "0/1"), ("Accuracy", "50%"));
        StudentRow(classPage, ana).Should().Equal("0/1", "0", "50%", "2026-03-10 14:00");

        var studentPage = await PageTextAsync(client, StudentUrl(ana));
        Stats(studentPage).Should().Equal(("Routines", "1"), ("Routines finished", "0/1"), ("Late", "0"), ("Accuracy", "50%"));
        Text(studentPage).Should().Contain("2 of 4 answered · Accuracy: 50%");
        Row(studentPage, "<div>1. Guess Note</div>").Should().Equal("1. Guess Note", "2/4", "50%", "6 s");

        foreach (var page in new[] { routinePage, classPage, studentPage })
        {
            page.Should().NotContain("Brass Band").And.NotContain("Fanfare");
            page.Should().NotContain($">{ana.Email}</div>", "her email is her user name, which is shown once");
        }
    }

    [Fact]
    public async Task ReportsOnAnotherTeachersClassesRoutinesOrStudents_AreNotFound()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var otherTeacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(RoleNames.Student, "bruno");
        await ClassroomAsync(teacher, "Choir", ana);
        var band = await ClassroomAsync(otherTeacher, "Band", ana, bruno);
        var fanfare = await RoutineAsync(otherTeacher, "Fanfare", ("GuessNote", 2));
        var classAssignment = await AssignAsync(fanfare, classroomId: band);
        var personalAssignment = await AssignAsync(fanfare, studentId: ana.Id);
        // The teacher's routine in another teacher's class, which the app never makes.
        var scales = await RoutineAsync(teacher, "Scales", ("GuessNote", 2));
        var strayAssignment = await AssignAsync(scales, classroomId: band);

        var client = await SignedInClientAsync(teacher);
        foreach (var url in new[]
        {
            AssignmentUrl(classAssignment), AssignmentUrl(personalAssignment), AssignmentUrl(strayAssignment), AssignmentUrl(0),
            ClassroomUrl(band), ClassroomUrl(0),
            StudentUrl(bruno), "/Teacher/Dashboard/Student/nobody",
        })
        {
            (await StatusAsync(client, url)).Should().Be(HttpStatusCode.NotFound, url);
        }
        (await PageTextAsync(client, StudentUrl(ana))).Should().Contain(NoRoutines).And.NotContain("Fanfare");

        var owner = await SignedInClientAsync(otherTeacher);
        foreach (var url in new[] { AssignmentUrl(classAssignment), AssignmentUrl(personalAssignment), ClassroomUrl(band), StudentUrl(bruno) })
        {
            (await StatusAsync(owner, url)).Should().Be(HttpStatusCode.OK, url);
        }

        var student = await SignedInClientAsync(ana);
        var response = await student.GetAsync(AssignmentUrl(classAssignment));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        new Uri(BaseAddress, response.Headers.Location!).AbsolutePath.Should().Be("/Identity/Account/AccessDenied");
    }

    [Fact]
    public async Task StudentsWhoLeaveTheTeachersClasses_LeaveTheReports()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var otherTeacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(RoleNames.Student, "bruno");
        var choir = await ClassroomAsync(teacher, "Choir", ana, bruno);
        var band = await ClassroomAsync(teacher, "Band", bruno);
        // Another teacher's class doesn't make the student the teacher's.
        await ClassroomAsync(otherTeacher, "Orchestra", bruno);
        var intervals = await RoutineAsync(teacher, "Intervals", ("GuessNote", 2));
        var rhythm = await RoutineAsync(teacher, "Rhythm", ("GuessNote", 2));
        var solo = await RoutineAsync(teacher, "Solo", ("GuessNote", 2));
        var intervalsId = await AssignAsync(intervals, classroomId: choir, assignedAt: Now.AddDays(-3));
        var rhythmId = await AssignAsync(rhythm, classroomId: band, assignedAt: Now.AddDays(-2));
        var soloId = await AssignAsync(solo, studentId: bruno.Id, assignedAt: Now.AddDays(-1));
        await AnswerAsync(intervalsId, intervals, 0, ana, Now.AddHours(-3), 5, true);
        await AnswerAsync(intervalsId, intervals, 0, bruno, Now.AddHours(-3), 5, true, false);
        await AnswerAsync(rhythmId, rhythm, 0, bruno, Now.AddHours(-2), 5, true);
        await AnswerAsync(soloId, solo, 0, bruno, Now.AddHours(-1), 5, false);

        // In two of the teacher's classes, a student takes their own routine once.
        Students(await AssignmentReportAsync(teacher, soloId)).Should().Equal(bruno.Id);
        Routines(await ClassroomReportAsync(teacher, choir)).Should().Equal("Solo", "Intervals");
        Routines(await ClassroomReportAsync(teacher, band)).Should().Equal("Solo", "Rhythm");
        Routines(await StudentReportAsync(teacher, bruno)).Should().Equal("Solo", "Rhythm", "Intervals");

        var client = await SignedInClientAsync(teacher);
        await RemoveMemberAsync(client, choir, bruno);

        var intervalsReport = (await AssignmentReportAsync(teacher, intervalsId))!;
        Students(intervalsReport).Should().Equal(ana.Id);
        (intervalsReport.Items.Single().Students, intervalsReport.Items.Single().Attempts).Should().Be((1, 1));
        var choirReport = await ClassroomReportAsync(teacher, choir);
        Routines(choirReport).Should().Equal("Intervals");
        choirReport!.Students.Select(s => s.Student.Id).Should().Equal(ana.Id);
        foreach (var url in new[] { AssignmentUrl(intervalsId), ClassroomUrl(choir) })
        {
            (await PageTextAsync(client, url)).Should().NotContain(bruno.UserName!, url);
        }
        var brunoReport = await StudentReportAsync(teacher, bruno);
        Routines(brunoReport).Should().Equal("Solo", "Rhythm");
        brunoReport!.Classrooms.Select(c => c.Name).Should().Equal("Band");

        // An archived class keeps its students.
        await ArchiveAsync(band);
        Students(await AssignmentReportAsync(teacher, soloId)).Should().Equal(bruno.Id);
        Routines(await ClassroomReportAsync(teacher, band)).Should().Equal("Solo", "Rhythm");
        Routines(await StudentReportAsync(teacher, bruno)).Should().Equal("Solo", "Rhythm");

        // Out of the teacher's last class, the student is no longer theirs to report on.
        await RemoveMemberAsync(client, band, bruno);
        (await StudentReportAsync(teacher, bruno)).Should().BeNull();
        (await StatusAsync(client, StudentUrl(bruno))).Should().Be(HttpStatusCode.NotFound);
        Students(await AssignmentReportAsync(teacher, rhythmId)).Should().BeEmpty();
        Students(await AssignmentReportAsync(teacher, soloId)).Should().BeEmpty();
        (await PageTextAsync(client, AssignmentUrl(soloId))).Should().Contain(NoStudents);
    }

    // ----- How a take is going -----

    [Fact]
    public async Task Takes_AreFinishedInProgressOrNotStarted_AndLateOnceDue()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(RoleNames.Student, "bruno");
        var carla = await CreateUserAsync(RoleNames.Student, "carla");
        var davi = await CreateUserAsync(RoleNames.Student, "davi");
        var choir = await ClassroomAsync(teacher, "Choir", ana, bruno, carla, davi);
        var exam = await RoutineAsync(teacher, "Exam", ("GuessNote", 2), ("HigherOrLower", 1));
        // Due on Sunday, taking late answers; it is Tuesday now.
        var examId = await AssignAsync(exam, classroomId: choir, dueAt: new DateTime(2026, 3, 8), allowLate: true,
            assignedAt: new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        var saturday = new DateTime(2026, 3, 7, 10, 0, 0, DateTimeKind.Utc);
        await AnswerAsync(examId, exam, 0, ana, saturday, 5, true, true);
        await AnswerAsync(examId, exam, 1, ana, saturday, 5, true);
        await AnswerAsync(examId, exam, 0, bruno, saturday.AddDays(1), 5, true, false);
        await AnswerAsync(examId, exam, 1, bruno, saturday.AddDays(2), 5, false);
        await AnswerAsync(examId, exam, 0, carla, saturday.AddHours(23), 5, true);

        var report = (await AssignmentReportAsync(teacher, examId))!;
        report.Takes.Select(t => (t.Student.Id, t.Status, t.IsLate)).Should().Equal(
            (ana.Id, TakeStatus.Finished, false),
            (bruno.Id, TakeStatus.Finished, true),
            (carla.Id, TakeStatus.InProgress, true),
            (davi.Id, TakeStatus.NotStarted, true));
        report.Totals.Should().Be(new TakeTotals(Takes: 4, Finished: 2, InProgress: 1, NotStarted: 1, Late: 3, Attempts: 7, Correct: 5));

        var client = await SignedInClientAsync(teacher);
        var page = await PageTextAsync(client, AssignmentUrl(examId));
        Stats(page).Should().Equal(
            ("Students", "4"), ("Finished", "2"), ("In progress", "1"), ("Not started", "1"), ("Late", "3"), ("Accuracy", "71%"));
        Row(page, "<div>1. Guess Note</div>").Should().Equal("1. Guess Note target: 2", "2/4", "5", "80%", "5 s");
        Row(page, "<div>2. Higher or Lower</div>").Should().Equal("2. Higher or Lower target: 1", "2/4", "2", "50%", "5 s");
        StudentRow(page, ana).Should().Equal("Finished", "2/2 100%", "1/1 100%", "100%", "2026-03-07 10:00");
        StudentRow(page, bruno).Should().Equal("Finished Late", "2/2 50%", "1/1 0%", "33%", "2026-03-09 10:00");
        StudentRow(page, carla).Should().Equal("In progress Late", "1/2 100%", "0/1 —", "100%", "—");
        StudentRow(page, davi).Should().Equal("Not started Late", "0/2 —", "0/1 —", "—", "—");

        var classPage = await PageTextAsync(client, ClassroomUrl(choir));
        Stats(classPage).Should().Equal(("Students", "4"), ("Routines", "1"), ("Routines finished", "2/4"), ("Accuracy", "71%"));
        Row(classPage, $"href=\"{AssignmentUrl(examId)}\"")[1..].Should().Equal("2026-03-01", "2026-03-08", "2/4", "3", "71%");
        StudentRow(classPage, ana).Should().Equal("1/1", "0", "100%", "2026-03-07 10:00");
        StudentRow(classPage, bruno).Should().Equal("1/1", "1", "33%", "2026-03-09 10:00");
        StudentRow(classPage, carla).Should().Equal("0/1", "1", "100%", "2026-03-08 09:00");
        StudentRow(classPage, davi).Should().Equal("0/1", "1", "—", "—");

        var studentPage = await PageTextAsync(client, StudentUrl(bruno));
        Stats(studentPage).Should().Equal(("Routines", "1"), ("Routines finished", "1/1"), ("Late", "1"), ("Accuracy", "33%"));
        Text(studentPage).Should().Contain("Choir · assigned 2026-03-01 · due 2026-03-08 · Late answers accepted");
        Text(studentPage).Should().Contain("3 of 3 answered · Accuracy: 33% · Finished on: 2026-03-09 10:00 Finished Late");
        Row(studentPage, "<div>2. Higher or Lower</div>").Should().Equal("2. Higher or Lower", "1/1", "0%", "5 s");
    }

    [Fact]
    public async Task LateAndDates_FollowTheTeachersTimeZone()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var night = await RoutineAsync(teacher, "Night", ("GuessNote", 1));
        var nightId = await AssignAsync(night, classroomId: choir, dueAt: new DateTime(2026, 3, 8), allowLate: true,
            assignedAt: new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc));
        // 11 pm on the due date in Toronto is already the next day in UTC.
        await AnswerAsync(nightId, night, 0, ana, new DateTime(2026, 3, 9, 3, 0, 0, DateTimeKind.Utc), 5, true);

        var utc = await PageTextAsync(await SignedInClientAsync(teacher), AssignmentUrl(nightId));
        var toronto = await PageTextAsync(await SignedInClientAsync(teacher, "America/Toronto"), AssignmentUrl(nightId));

        StudentRow(utc, ana).Should().Equal("Finished Late", "1/1 100%", "100%", "2026-03-09 03:00");
        Text(utc).Should().Contain("Choir · assigned 2026-03-01 · due 2026-03-08 · Late answers accepted");
        StudentRow(toronto, ana).Should().Equal("Finished", "1/1 100%", "100%", "2026-03-08 23:00");
        Text(toronto).Should().Contain("Choir · assigned 2026-02-28 · due 2026-03-08 · Late answers accepted");
    }

    [Fact]
    public async Task Takes_FollowEachStudentsAdjustments()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(RoleNames.Student, "bruno");
        var carla = await CreateUserAsync(RoleNames.Student, "carla");
        var choir = await ClassroomAsync(teacher, "Choir", ana, bruno, carla);
        var mixed = await RoutineAsync(teacher, "Mixed", ("GuessNote", 2), ("HigherOrLower", 1));
        var mixedId = await AssignAsync(mixed, classroomId: choir);
        await AdjustAsync(mixedId, mixed, 1, ana, exclude: true);
        await AdjustAsync(mixedId, mixed, 0, bruno, target: 1);
        await AdjustAsync(mixedId, mixed, 0, carla, exclude: true);
        await AdjustAsync(mixedId, mixed, 1, carla, exclude: true);
        await AnswerAsync(mixedId, mixed, 0, ana, Now.AddHours(-1), 5, true, true);
        await AnswerAsync(mixedId, mixed, 0, bruno, Now.AddHours(-1), 5, true);
        await AnswerAsync(mixedId, mixed, 1, bruno, Now.AddHours(-1), 5, false);

        var report = (await AssignmentReportAsync(teacher, mixedId))!;
        report.Takes.Select(t => (t.Student.Id, t.Status, t.Answered, t.Questions)).Should().Equal(
            (ana.Id, TakeStatus.Finished, 2, 2),
            (bruno.Id, TakeStatus.Finished, 2, 2));
        report.Items.Select(i => (i.Students, i.Completed)).Should().Equal((2, 2), (1, 1));

        var client = await SignedInClientAsync(teacher);
        var page = await PageTextAsync(client, AssignmentUrl(mixedId));
        Row(page, "<div>1. Guess Note</div>").Should().Equal("1. Guess Note target: 2", "2/2", "3", "100%", "5 s");
        Row(page, "<div>2. Higher or Lower</div>").Should().Equal("2. Higher or Lower target: 1", "1/1", "1", "0%", "5 s");
        StudentRow(page, ana).Should().Equal("Finished", "2/2 100%", $"— {Excluded}", "100%", "2026-03-10 14:00");
        StudentRow(page, bruno).Should().Equal("Finished", "1/1 100%", "1/1 0%", "50%", "2026-03-10 14:00");
        page.Should().NotContain(carla.UserName!, "every exercise of the routine is excluded for her");

        var anaPage = await PageTextAsync(client, StudentUrl(ana));
        Text(anaPage).Should().Contain("2 of 2 answered");
        anaPage.Should().Contain($"<td colspan=\"3\" class=\"text-end text-secondary\">{Excluded}</td>");
        Row(anaPage, "<div>2. Higher or Lower</div>").Should().Equal("2. Higher or Lower", Excluded);
        (await PageTextAsync(client, StudentUrl(carla))).Should().Contain(NoRoutines);
    }

    [Fact]
    public async Task AnswersToARemovedAssignment_CountNowhere()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var quiz = await RoutineAsync(teacher, "Quiz", ("GuessNote", 2));
        var oldId = await AssignAsync(quiz, classroomId: choir, assignedAt: Now.AddDays(-2));
        await AnswerAsync(oldId, quiz, 0, ana, Now.AddDays(-1), 5, true, true);
        (await AssignmentReportAsync(teacher, oldId))!.Takes.Single().Status.Should().Be(TakeStatus.Finished);

        var client = await SignedInClientAsync(teacher);
        var response = await PostAsync(client, "/Teacher/Routines/Unassign", new()
        {
            ["routineId"] = Id(quiz.Id),
            ["assignmentId"] = Id(oldId),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var newId = await AssignAsync(quiz, classroomId: choir, assignedAt: Now.AddHours(-1));

        (await AssignmentReportAsync(teacher, oldId)).Should().BeNull();
        (await StatusAsync(client, AssignmentUrl(oldId))).Should().Be(HttpStatusCode.NotFound);
        var take = (await AssignmentReportAsync(teacher, newId))!.Takes.Should().ContainSingle().Subject;
        (take.Status, take.Attempts).Should().Be((TakeStatus.NotStarted, 0));
        var classroom = (await ClassroomReportAsync(teacher, choir))!;
        classroom.Routines.Select(r => r.Assignment.Id).Should().Equal(newId);
        var row = classroom.Students.Should().ContainSingle().Subject;
        (row.Totals.Attempts, row.LastAnswerAt).Should().Be((0, (DateTime?)null));
        (await StudentReportAsync(teacher, ana))!.Routines.Select(r => (r.Assignment.Id, r.Take.Status))
            .Should().Equal((newId, TakeStatus.NotStarted));
        (await PageTextAsync(client, StudentUrl(ana))).Should().Contain("0 of 2 answered")
            .And.NotContain($"href=\"{AssignmentUrl(oldId)}\"");
    }

    // ----- Getting around -----

    [Fact]
    public async Task Reports_LinkToEachOther()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var scales = await RoutineAsync(teacher, "Scales", ("GuessNote", 1));
        var classId = await AssignAsync(scales, classroomId: choir);
        var personalId = await AssignAsync(scales, studentId: ana.Id);
        var client = await SignedInClientAsync(teacher);

        var links = new Dictionary<string, string[]>
        {
            [$"/Teacher/Routines/Details/{Id(scales.Id)}"] = [AssignmentUrl(classId), AssignmentUrl(personalId)],
            [$"/Teacher/Classrooms/Details/{Id(choir)}"] = [ClassroomUrl(choir)],
            [AssignmentUrl(classId)] = [StudentUrl(ana, $"?assignmentId={Id(classId)}"), ClassroomUrl(choir)],
            [AssignmentUrl(personalId)] = [StudentUrl(ana, $"?assignmentId={Id(personalId)}")],
            [ClassroomUrl(choir)] = [StudentUrl(ana, $"?classroomId={Id(choir)}"), AssignmentUrl(classId), AssignmentUrl(personalId)],
            [StudentUrl(ana)] = [AssignmentUrl(classId), AssignmentUrl(personalId), ClassroomUrl(choir)],
        };
        foreach (var (page, expected) in links)
        {
            Hrefs(await PageTextAsync(client, page)).Should().Contain(expected, page);
            foreach (var link in expected)
            {
                (await StatusAsync(client, link)).Should().Be(HttpStatusCode.OK, link);
            }
        }
    }

    [Fact]
    public async Task Reports_GoBackWhereTheTeacherCameFrom()
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var bruno = await CreateUserAsync(RoleNames.Student, "bruno");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var band = await ClassroomAsync(teacher, "Band", ana);
        var strings = await ClassroomAsync(teacher, "Strings", bruno);
        var scales = await RoutineAsync(teacher, "Scales", ("GuessNote", 1));
        var choirId = await AssignAsync(scales, classroomId: choir);
        var stringsId = await AssignAsync(scales, classroomId: strings);
        var client = await SignedInClientAsync(teacher);

        var routinePage = await PageTextAsync(client, AssignmentUrl(choirId));
        (BackLink(routinePage), ActiveSection(routinePage)).Should().Be(($"/Teacher/Routines/Details/{Id(scales.Id)}", "/Teacher/Routines"));
        var classPage = await PageTextAsync(client, ClassroomUrl(choir));
        (BackLink(classPage), ActiveSection(classPage)).Should().Be(($"/Teacher/Classrooms/Details/{Id(choir)}", "/Teacher/Classrooms"));

        // A student's page goes back to the report the teacher came from, when that report is about them.
        foreach (var (query, back) in new[]
        {
            ($"?assignmentId={Id(choirId)}", AssignmentUrl(choirId)),
            ($"?assignmentId={Id(choirId)}&classroomId={Id(band)}", AssignmentUrl(choirId)),
            ($"?classroomId={Id(band)}", ClassroomUrl(band)),
            ($"?assignmentId={Id(stringsId)}&classroomId={Id(band)}", ClassroomUrl(band)),
            ($"?assignmentId={Id(stringsId)}", "/Teacher/Classrooms"),
            ($"?classroomId={Id(strings)}", "/Teacher/Classrooms"),
            ("", "/Teacher/Classrooms"),
        })
        {
            var page = await PageTextAsync(client, StudentUrl(ana, query));
            (BackLink(page), ActiveSection(page)).Should().Be((back, "/Teacher/Classrooms"), query);
        }
    }

    [Theory]
    [InlineData("en-US", "Routine report", "Class report", "Student report", "Higher or Lower")]
    [InlineData("pt-BR", "Relatório da rotina", "Relatório da turma", "Relatório do aluno", "Mais alto ou mais grave")]
    [InlineData("fr-CA", "Rapport de routine", "Rapport de classe", "Rapport d’élève", "Plus aigu ou plus grave")]
    public async Task Reports_AreInThePageLanguage(string culture, string routineTitle, string classTitle, string studentTitle, string higherOrLower)
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var ana = await CreateUserAsync(RoleNames.Student, "ana");
        var choir = await ClassroomAsync(teacher, "Choir", ana);
        var ears = await RoutineAsync(teacher, "Ears", ("HigherOrLower", 2));
        var earsId = await AssignAsync(ears, classroomId: choir);
        await AnswerAsync(earsId, ears, 0, ana, Now.AddHours(-1), 5, true);
        var client = await SignedInClientAsync(teacher);

        foreach (var (url, title, namesExercises) in new[]
        {
            ($"{AssignmentUrl(earsId)}?culture={culture}", routineTitle, true),
            ($"{ClassroomUrl(choir)}?culture={culture}", classTitle, false),
            ($"{StudentUrl(ana)}?classroomId={Id(choir)}&culture={culture}", studentTitle, true),
        })
        {
            var page = await PageTextAsync(client, url);
            page.Should().Contain($"<span class=\"aa-eyebrow\">{title}</span>", url)
                .And.NotContain("Teacher.Reports.", url)
                .And.NotContain(">HigherOrLower<", url);
            if (namesExercises) page.Should().Contain($"<div>1. {higherOrLower}</div>", url);
        }
    }

    // ----- Helpers -----

    /// <param name="ItemIds">The routine's items, in order.</param>
    /// <param name="ExerciseIds">Each item's exercise.</param>
    private sealed record TestRoutine(int Id, int[] ItemIds, int[] ExerciseIds);

    // One of the teacher's classrooms, with these students in it.
    private Task<int> ClassroomAsync(ApplicationUser teacher, string name, params ApplicationUser[] students) => DbAsync(async db =>
    {
        var classroom = new Classroom { Name = name, OwnerId = teacher.Id };
        foreach (var student in students)
        {
            classroom.Members.Add(new ClassroomMember { StudentId = student.Id });
        }
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        return classroom.Id;
    });

    // A routine of the teacher's: these exercises in order, each with its number of questions.
    private Task<TestRoutine> RoutineAsync(ApplicationUser teacher, string name, params (string Exercise, int Target)[] items) => DbAsync(async db =>
    {
        if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
        var routine = new Routine { Name = name, OwnerId = teacher.Id };
        for (var n = 0; n < items.Length; n++)
        {
            var exercise = items[n].Exercise;
            var exerciseId = await db.Exercises.Where(e => e.Name == exercise).Select(e => e.ExerciseId).SingleAsync();
            routine.Items.Add(new RoutineItem { ExerciseId = exerciseId, Order = n + 1, TargetCount = items[n].Target });
        }
        db.Routines.Add(routine);
        await db.SaveChangesAsync();
        var ordered = routine.Items.OrderBy(i => i.Order).ToList();
        return new TestRoutine(routine.Id, ordered.Select(i => i.Id).ToArray(), ordered.Select(i => i.ExerciseId).ToArray());
    });

    // Assigns the routine to a classroom or to a student, the day before unless told when.
    private Task<int> AssignAsync(
        TestRoutine routine,
        int? classroomId = null,
        string? studentId = null,
        DateTime? dueAt = null,
        bool allowLate = false,
        DateTime? assignedAt = null) => DbAsync(async db =>
    {
        var assignment = new RoutineAssignment
        {
            RoutineId = routine.Id,
            ClassroomId = classroomId,
            StudentId = studentId,
            AssignedAt = assignedAt ?? Now.AddDays(-1),
            DueAt = dueAt,
            AllowLate = allowLate,
        };
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    });

    // The student's next answers to an item of the assignment, right or wrong in turn, each taking these seconds.
    private Task AnswerAsync(
        int assignmentId, TestRoutine routine, int item, ApplicationUser student, DateTime at, int seconds, params bool[] correct)
        => DbAsync(async db =>
        {
            var itemId = routine.ItemIds[item];
            var asked = await db.ScoreSnapshots.CountAsync(s =>
                s.UserId == student.Id && s.RoutineAssignmentId == assignmentId && s.RoutineItemId == itemId);
            foreach (var isCorrect in correct)
            {
                db.ScoreSnapshots.Add(new ScoreSnapshot
                {
                    UserId = student.Id,
                    ExerciseId = routine.ExerciseIds[item],
                    IsCorrect = isCorrect,
                    TimeSpentSeconds = seconds,
                    Timestamp = at,
                    RoutineAssignmentId = assignmentId,
                    RoutineItemId = itemId,
                    RoutineQuestion = ++asked,
                });
            }
            await db.SaveChangesAsync();
        });

    // Practice outside routines: answers without an assignment, and their running totals.
    private Task PracticeAsync(ApplicationUser student, string exercise, DateTime at, int right, int wrong) => DbAsync(async db =>
    {
        var exerciseId = await db.Exercises.Where(e => e.Name == exercise).Select(e => e.ExerciseId).SingleAsync();
        for (var n = 0; n < right + wrong; n++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = student.Id, ExerciseId = exerciseId, IsCorrect = n < right, TimeSpentSeconds = 4, Timestamp = at,
            });
        }
        db.Add(new ScoreAggregate
        {
            UserId = student.Id, ExerciseId = exerciseId, CorrectCount = right, ErrorCount = wrong, BestScore = right, LastAttemptAt = at,
        });
        await db.SaveChangesAsync();
    });

    // The student's adjustment of an item of the assignment: left out, or their own number of questions.
    private Task AdjustAsync(int assignmentId, TestRoutine routine, int item, ApplicationUser student, bool exclude = false, int? target = null)
        => DbAsync(db =>
        {
            db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
            {
                RoutineAssignmentId = assignmentId,
                StudentId = student.Id,
                RoutineItemId = routine.ItemIds[item],
                ExcludeItem = exclude,
                OverrideTargetCount = target,
            });
            return db.SaveChangesAsync();
        });

    // Takes the student out of the classroom with the class page's Remove button.
    private async Task RemoveMemberAsync(HttpClient teacherClient, int classroomId, ApplicationUser student)
    {
        var memberId = await DbAsync(db => db.ClassroomMembers
            .Where(m => m.ClassroomId == classroomId && m.StudentId == student.Id)
            .Select(m => m.Id)
            .SingleAsync());
        var response = await PostAsync(teacherClient, "/Teacher/Members/Remove", new()
        {
            ["classroomId"] = Id(classroomId),
            ["memberId"] = Id(memberId),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private Task ArchiveAsync(int classroomId) => DbAsync(async db =>
    {
        (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).IsArchived = true;
        await db.SaveChangesAsync();
    });

    private Task<AssignmentReport?> AssignmentReportAsync(ApplicationUser teacher, int assignmentId)
        => ReportsAsync(reports => reports.AssignmentAsync(teacher.Id, assignmentId, TimeZoneInfo.Utc));

    private Task<ClassroomReport?> ClassroomReportAsync(ApplicationUser teacher, int classroomId)
        => ReportsAsync(reports => reports.ClassroomAsync(teacher.Id, classroomId, TimeZoneInfo.Utc));

    private Task<StudentReport?> StudentReportAsync(ApplicationUser teacher, ApplicationUser student)
        => ReportsAsync(reports => reports.StudentAsync(teacher.Id, student.Id, TimeZoneInfo.Utc));

    private async Task<T> ReportsAsync<T>(Func<RoutineReports, Task<T>> report)
    {
        using var scope = _factory.Services.CreateScope();
        return await report(scope.ServiceProvider.GetRequiredService<RoutineReports>());
    }

    // The students taking the assignment, in the report's order.
    private static IEnumerable<string> Students(AssignmentReport? report) => report!.Takes.Select(t => t.Student.Id);

    // The routines in the report, latest assigned first.
    private static IEnumerable<string> Routines(ClassroomReport? report) => report!.Routines.Select(r => r.Assignment.RoutineName);

    private static IEnumerable<string> Routines(StudentReport? report) => report!.Routines.Select(r => r.Assignment.RoutineName);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task DbAsync(Func<ApplicationDbContext, Task> work)
    {
        using var scope = _factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static string AssignmentUrl(int id) => $"/Teacher/Dashboard/Assignment/{Id(id)}";

    private static string ClassroomUrl(int id) => $"/Teacher/Dashboard/Classroom/{Id(id)}";

    private static string StudentUrl(ApplicationUser student, string query = "") => $"/Teacher/Dashboard/Student/{student.Id}{query}";

    // How a report's table links to a student's report.
    private static string StudentLink(ApplicationUser student) => $"href=\"{StudentUrl(student)}?";

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    // The figures at the top of a report.
    private static List<(string Label, string Value)> Stats(string page)
        => Regex.Matches(page, "<div class=\"text-secondary small\">(.*?)</div>\\s*<div class=\"h3 mb-0\">(.*?)</div>", RegexOptions.Singleline)
            .Select(m => (Text(m.Groups[1].Value), Text(m.Groups[2].Value)))
            .ToList();

    // The text of each cell of the one table row whose markup has this.
    private static string[] Row(string page, string has)
    {
        var rows = Regex.Matches(page, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(row => row.Contains(has, StringComparison.Ordinal))
            .ToList();
        rows.Should().ContainSingle("one row has {0}", has);
        return Regex.Matches(rows[0], "<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline)
            .Select(m => Text(m.Groups[1].Value))
            .ToArray();
    }

    // A student's row in a report's table, after the cell naming them.
    private static string[] StudentRow(string page, ApplicationUser student) => Row(page, StudentLink(student))[1..];

    private static string Text(string html) => Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " ").Trim();

    private static List<string> Hrefs(string page) => Regex.Matches(page, "href=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

    private static string BackLink(string page)
        => Regex.Matches(page, "<a class=\"btn btn-link btn-sm\" href=\"([^\"]*)\">Back</a>")
            .Select(m => m.Groups[1].Value).Should().ContainSingle().Subject;

    // Where the sidebar's current section leads.
    private static string ActiveSection(string page)
        => Regex.Matches(page, "<a class=\"nav-link rounded-2 active\" href=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value).Should().ContainSingle().Subject;

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        return response.StatusCode;
    }

    // Posts a form, with an antiforgery token from another page.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await PageAsync(client, "/Home/Privacy"));
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
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

    // Reports sort students by user name, so tests name them in alphabetical order.
    private async Task<ApplicationUser> CreateUserAsync(string role, string? name = null)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            (await roles.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{name ?? role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
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

    // Signs in through the real form, from a browser in this time zone (UTC when none).
    private async Task<HttpClient> SignedInClientAsync(ApplicationUser user, string? timeZone = null)
    {
        var cookies = new CookieContainer();
        if (timeZone is not null)
        {
            cookies.Add(BaseAddress, new Cookie(UserTimeZone.CookieName, Uri.EscapeDataString(timeZone)));
        }
        var client = _factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));
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

    /// <summary><see cref="TestWebApplicationFactory"/> whose clock stands still at <see cref="Now"/>.</summary>
    public sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new AnswerTimeTests.ManualClock(new DateTimeOffset(Now)));
            });
        }
    }
}
