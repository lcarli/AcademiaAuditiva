using System.Globalization;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Email;
using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Notification e-mails on SQL Server: every replica takes from one count of the day's e-mails
/// (<see cref="EmailDailyCount"/>), and the students to e-mail are read with real SQL.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class NotificationEmailSqlTests
{
    private readonly RealSqlServerFixture _fixture;

    public NotificationEmailSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task TheDailyLimit_HoldsWhenManyTakeFromItAtOnce()
    {
        await MigrateAsync();
        // A day no other test counts e-mails on: they share the database.
        var day = new DateTimeOffset(2031, 3, 14, 12, 0, 0, TimeSpan.Zero);
        var quota = Quota(limit: 10, new AnswerTimeTests.ManualClock(day));

        var granted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => quota.ReserveAsync(3))));

        granted.Sum().Should().Be(10, "eight requests of 3 want more than the day's 10, and none may be lost or overspent");
        await using var db = _fixture.CreateContext();
        (await db.EmailDailyCounts.SingleAsync(c => c.Day == DateOnly.FromDateTime(day.UtcDateTime))).Sent.Should().Be(10);
        (await quota.ReserveAsync(1)).Should().Be(0);
    }

    [RealSqlFact]
    public async Task TheChosenStudentsWhoGetTheseEmails_AreEmailed_EachInTheirLanguage()
    {
        await MigrateAsync();
        var teacher = NewUser("email-teacher");
        var ana = NewUser("email-ana");
        var bruno = NewUser("email-bruno", emailsOff: true);
        var carla = NewUser("email-carla", confirmed: false);
        var davi = NewUser("email-davi");
        var elisa = NewUser("email-elisa", language: "pt-BR");
        int assignmentId;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana, bruno, carla, davi, elisa);
            var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
            var assignment = new RoutineAssignment
            {
                Routine = new Routine
                {
                    Name = "SQL e-mailed routine",
                    OwnerId = teacher.Id,
                    Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1 } }
                },
                Classroom = new Classroom
                {
                    Name = "SQL e-mailed classroom",
                    OwnerId = teacher.Id,
                    Members = { Member(ana), Member(bruno), Member(carla), Member(davi), Member(elisa) }
                },
                ChosenStudentsOnly = true,
                ChosenStudents = { Chosen(ana), Chosen(bruno), Chosen(carla), Chosen(elisa) }
            };
            db.RoutineAssignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        var queue = new BackgroundEmailQueue();
        var sender = new RecordingEmailSender();
        var count = await WithRoutineEmailsAsync(queue, sender, emails => emails.QueueAsync(assignmentId));

        count.Should().Be(new RoutineEmailCount(Students: 2, Queued: 2), "of the chosen, Bruno turned these e-mails off and Carla hasn't confirmed her address");
        queue.Pending.Should().Be(1);

        await WithRoutineEmailsAsync(queue, sender, async emails =>
        {
            await emails.SendAsync(new RoutineEmailJob(assignmentId, [ana.Id, davi.Id, elisa.Id], "fr-CA"), CancellationToken.None);
            return 0;
        });

        sender.Sent.Select(s => (s.To, Lang(s.Message.HtmlBody))).Should().Equal(
            [(ana.Email!, "fr-CA"), (elisa.Email!, "pt-BR")], "Davi wasn't chosen, and Ana never chose a language");
        sender.Sent.First().Message.Subject.Should().Be("Nouvelle routine à pratiquer\u00a0: SQL e-mailed routine");
    }

    private async Task MigrateAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
    }

    private NotificationEmailQuota Quota(int limit, TimeProvider clock) => new(
        _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
        clock,
        new FixedOptions<NotificationEmailOptions>(new() { DailyLimit = limit }),
        NullLogger<NotificationEmailQuota>.Instance);

    // RoutineEmails as the app builds it for a request, with e-mail set up and the fixture's database.
    private async Task<T> WithRoutineEmailsAsync<T>(BackgroundEmailQueue queue, RecordingEmailSender sender, Func<RoutineEmails, Task<T>> work)
    {
        var cultures = new[] { "fr-CA", "en-US", "pt-BR" }.Select(CultureInfo.GetCultureInfo).ToList();
        var options = new NotificationEmailOptions { DailyLimit = 100 };
        using var scope = _fixture.Services.CreateScope();
        var emails = new RoutineEmails(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            Options.Create(new SmtpOptions
            {
                Host = "smtp.resend.com",
                User = "resend",
                Password = "x",
                FromAddress = "no-reply@academiaauditiva.com"
            }),
            new FixedOptions<NotificationEmailOptions>(options),
            Quota(options.DailyLimit, new AnswerTimeTests.ManualClock(new DateTimeOffset(2031, 3, 15, 12, 0, 0, TimeSpan.Zero))),
            queue,
            new EmailComposer(
                new ServiceCollection().BuildServiceProvider(),
                NullLoggerFactory.Instance,
                new StringLocalizer<SharedResources>(new ResourceManagerStringLocalizerFactory(
                    Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance))),
            sender,
            Options.Create(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en-US"),
                SupportedCultures = cultures,
                SupportedUICultures = cultures
            }),
            NullLogger<RoutineEmails>.Instance);
        return await work(emails);
    }

    private sealed class FixedOptions<T>(T value) : IOptionsSnapshot<T>, IOptionsMonitor<T> where T : class
    {
        public T Value => value;
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static string Lang(string html) => Regex.Match(html, "<html lang=\"([^\"]+)\"").Groups[1].Value;

    private static ClassroomMember Member(ApplicationUser student) => new() { StudentId = student.Id };

    private static RoutineAssignmentStudent Chosen(ApplicationUser student) => new() { StudentId = student.Id };

    private static ApplicationUser NewUser(string prefix, string? language = null, bool confirmed = true, bool emailsOff = false)
    {
        var email = $"{prefix}.{Guid.NewGuid():N}@example.test";
        return new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            FirstName = "Sql",
            LastName = "Tester",
            Language = language,
            RoutineEmailsOff = emailsOff
        };
    }
}
