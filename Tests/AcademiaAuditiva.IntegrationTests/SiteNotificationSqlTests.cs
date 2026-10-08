using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Notifications;
using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Site notifications on SQL Server: the unique index keeps each student told once when requests
/// and replicas race, old notifications are deleted, the foreign keys let accounts and assignments
/// go, and the bell's queries and the hourly job run with real SQL.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class SiteNotificationSqlTests
{
    private const int Racers = 6;

    private readonly RealSqlServerFixture _fixture;

    public SiteNotificationSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task NotifiersRacingEachOther_TellEachOneOnce()
    {
        await MigrateAsync();
        var clock = Clock(2031, 7, 1, 9);
        var teacher = NewUser("race-teacher");
        var ana = NewUser("race-ana");
        var bruno = NewUser("race-bruno");
        var carla = NewUser("race-carla");
        int wholeClass, chosenOnly;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana, bruno, carla);
            var routine = await RoutineAsync(db, teacher, "SQL raced routine");
            var classroom = NewClassroom(teacher, "SQL raced classroom", teacher, ana, bruno, carla);
            var toClass = new RoutineAssignment { Routine = routine, Classroom = classroom };
            var toSome = new RoutineAssignment
            {
                Routine = routine,
                Classroom = classroom,
                ChosenStudentsOnly = true,
                ChosenStudents = { new RoutineAssignmentStudent { StudentId = ana.Id }, new RoutineAssignmentStudent { StudentId = carla.Id } }
            };
            db.RoutineAssignments.AddRange(toClass, toSome);
            await db.SaveChangesAsync();
            db.ScoreSnapshots.Add(Answer(ana, toClass, routine));
            await db.SaveChangesAsync();
            wholeClass = toClass.Id;
            chosenOnly = toSome.Id;
        }

        var log = new RecordingLogger<Notifier>();
        await RaceAsync(async (notifier, i) =>
        {
            // Each in its own order, so that they meet on every one.
            Func<Task>[] steps =
            [
                () => notifier.RoutineAssignedAsync(wholeClass),
                () => notifier.RoutineAssignedAsync(chosenOnly),
                () => notifier.RoutineItemCompletedAsync(ana.Id, wholeClass),
            ];
            for (var k = 0; k < steps.Length; k++)
            {
                await steps[(i + k) % steps.Length]();
            }
            return 0;
        }, clock, log);

        var now = clock.GetUtcNow().UtcDateTime;
        (await RowsAsync(n => n.RoutineAssignmentId == wholeClass || n.RoutineAssignmentId == chosenOnly)).Should().BeEquivalentTo(
        [
            new Row(ana.Id, NotificationKind.RoutineAssigned, ana.Id, wholeClass, now),
            new Row(bruno.Id, NotificationKind.RoutineAssigned, bruno.Id, wholeClass, now),
            new Row(carla.Id, NotificationKind.RoutineAssigned, carla.Id, wholeClass, now),
            new Row(ana.Id, NotificationKind.RoutineAssigned, ana.Id, chosenOnly, now),
            new Row(carla.Id, NotificationKind.RoutineAssigned, carla.Id, chosenOnly, now),
            new Row(teacher.Id, NotificationKind.StudentFinishedRoutine, ana.Id, wholeClass, now),
        ], "the teacher is in the class but isn't told of their own routine, and Ana answered its one question");
        log.Problems().Should().BeEmpty("a notifier that loses the race finds the others told");
    }

    [RealSqlFact]
    public async Task StudentsAreRemindedOnce_EvenByManyRunsAtOnce()
    {
        await MigrateAsync();
        // No other test has a routine due on 11 September 2031: the reminders look at every routine.
        var clock = Clock(2031, 9, 10, 12, 30);
        var teacher = NewUser("remind-teacher");
        var ana = NewUser("remind-ana");
        var bruno = NewUser("remind-bruno");
        var carla = NewUser("remind-carla");
        var davi = NewUser("remind-davi");
        var elisa = NewUser("remind-elisa");
        int toClassId, toDaviId;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana, bruno, carla, davi, elisa);
            var routine = await RoutineAsync(db, teacher, "SQL reminded routine");
            var classroom = NewClassroom(teacher, "SQL reminded classroom", teacher, ana, bruno, carla);
            var archived = NewClassroom(teacher, "SQL archived classroom", elisa);
            archived.IsArchived = true;
            var assignedAt = Utc(2031, 9, 8);
            var dueAt = Utc(2031, 9, 11);
            var toClass = new RoutineAssignment { Routine = routine, Classroom = classroom, AssignedAt = assignedAt, DueAt = dueAt };
            var toDavi = new RoutineAssignment { Routine = routine, StudentId = davi.Id, AssignedAt = assignedAt, DueAt = dueAt };
            db.RoutineAssignments.AddRange(
                toClass,
                toDavi,
                // Assigned today: its students were just told of it, with its due date.
                new RoutineAssignment { Routine = routine, Classroom = classroom, AssignedAt = Utc(2031, 9, 10, 6), DueAt = dueAt },
                new RoutineAssignment { Routine = routine, StudentId = ana.Id, AssignedAt = assignedAt, DueAt = Utc(2031, 9, 12) },
                // An archived class's students no longer see its routines.
                new RoutineAssignment { Routine = routine, Classroom = archived, AssignedAt = assignedAt, DueAt = dueAt });
            await db.SaveChangesAsync();
            db.ScoreSnapshots.Add(Answer(carla, toClass, routine));
            await db.SaveChangesAsync();
            toClassId = toClass.Id;
            toDaviId = toDavi.Id;
        }

        var log = new RecordingLogger<Notifier>();
        var reminded = await RaceAsync((notifier, _) => notifier.RemindDueTomorrowAsync(), clock, log);

        reminded.Sum().Should().Be(3, "each student is reminded by one of the runs only");
        var now = clock.GetUtcNow().UtcDateTime;
        string[] students = [teacher.Id, ana.Id, bruno.Id, carla.Id, davi.Id, elisa.Id];
        (await RowsAsync(n => n.Kind == NotificationKind.RoutineDueTomorrow && students.Contains(n.StudentId))).Should().BeEquivalentTo(
        [
            new Row(ana.Id, NotificationKind.RoutineDueTomorrow, ana.Id, toClassId, now),
            new Row(bruno.Id, NotificationKind.RoutineDueTomorrow, bruno.Id, toClassId, now),
            new Row(davi.Id, NotificationKind.RoutineDueTomorrow, davi.Id, toDaviId, now),
        ], "Carla has finished the routine, and Elisa's class is archived");
        log.Problems().Should().BeEmpty("a run that loses the race finds the others told");

        await using (var db = _fixture.CreateContext())
        {
            (await NewNotifier(db, clock, log).RemindDueTomorrowAsync()).Should().Be(0, "the next hour's run finds them all reminded");
        }
    }

    [RealSqlFact]
    public async Task OldNotifications_AreDeleted_ReadOrNot()
    {
        await MigrateAsync();
        // 90 days before 1 May 2000, a leap year, is 1 February: no other test has notifications that old.
        var clock = Clock(2000, 5, 1, 0);
        var cutoff = Utc(2000, 2, 1);
        var teacher = NewUser("purge-teacher");
        var student = NewUser("purge-student");
        int classroomId;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, student);
            var classroom = NewClassroom(teacher, "SQL purged classroom", student);
            db.Notifications.AddRange(
                Notice(teacher, NotificationKind.InviteAccepted, student, cutoff.AddDays(-1), classroom: classroom, readAt: cutoff),
                Notice(teacher, NotificationKind.InviteAccepted, student, cutoff.AddTicks(-1), classroom: classroom),
                Notice(teacher, NotificationKind.InviteAccepted, student, cutoff, classroom: classroom),
                Notice(teacher, NotificationKind.InviteAccepted, student, cutoff.AddDays(1), classroom: classroom, readAt: cutoff.AddDays(2)));
            await db.SaveChangesAsync();
            classroomId = classroom.Id;
        }

        var log = new RecordingLogger<Notifier>();
        await using (var db = _fixture.CreateContext())
        {
            var notifier = NewNotifier(db, clock, log);
            (await notifier.PurgeAsync()).Should().Be(2);
            (await notifier.PurgeAsync()).Should().Be(0);
        }

        await using (var db = _fixture.CreateContext())
        {
            (await db.Notifications.Where(n => n.ClassroomId == classroomId).OrderBy(n => n.CreatedAt).Select(n => n.CreatedAt).ToListAsync())
                .Should().Equal([cutoff, cutoff.AddDays(1)], "those 90 days old or newer are kept");
        }
        log.Problems().Should().BeEmpty();
    }

    [RealSqlFact]
    public async Task DeletingAnAccount_DeletesItsNotifications_AndThoseAboutIt()
    {
        await MigrateAsync();
        var teacher = NewUser("delete-teacher");
        var ana = NewUser("delete-ana");
        var bruno = NewUser("delete-bruno");
        int[] ids;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana, bruno);
            var routine = await RoutineAsync(db, teacher, "SQL deleted routine");
            var classroom = NewClassroom(teacher, "SQL deleted classroom", ana, bruno);
            var toClass = new RoutineAssignment { Routine = routine, Classroom = classroom };
            var toAna = new RoutineAssignment { Routine = routine, StudentId = ana.Id };
            var at = Utc(2031, 4, 1);
            Notification[] notifications =
            [
                Notice(ana, NotificationKind.RoutineAssigned, ana, at, toClass),
                Notice(ana, NotificationKind.RoutineAssigned, ana, at, toAna),
                // The teacher's notifications about Ana: she is their student, whose deletion they restrict.
                Notice(teacher, NotificationKind.StudentFinishedRoutine, ana, at, toClass),
                Notice(teacher, NotificationKind.StudentFinishedRoutine, ana, at, toAna),
                Notice(teacher, NotificationKind.InviteAccepted, ana, at, classroom: classroom),
                Notice(bruno, NotificationKind.RoutineAssigned, bruno, at, toClass),
                Notice(teacher, NotificationKind.InviteAccepted, bruno, at, classroom: classroom),
            ];
            db.RoutineAssignments.AddRange(toClass, toAna);
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();
            ids = notifications.Select(n => n.Id).ToArray();
        }

        await DeleteAsync(ana.Id);
        (await RemainingAsync(ids)).Should().BeEquivalentTo([ids[5], ids[6]], "those about Bruno stay");

        await DeleteAsync(teacher.Id);
        (await RemainingAsync(ids)).Should().BeEmpty("Bruno's was about the teacher's routine, deleted with them");
        await using (var db = _fixture.CreateContext())
        {
            (await db.Users.AnyAsync(u => u.Id == bruno.Id)).Should().BeTrue();
        }
    }

    [RealSqlFact]
    public async Task UnassigningARoutine_DeletesItsNotifications()
    {
        await MigrateAsync();
        var teacher = NewUser("unassign-teacher");
        var ana = NewUser("unassign-ana");
        int unassigned;
        int[] ids;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana);
            var routine = await RoutineAsync(db, teacher, "SQL unassigned routine");
            var first = new RoutineAssignment { Routine = routine, StudentId = ana.Id };
            var second = new RoutineAssignment { Routine = routine, StudentId = ana.Id };
            var at = Utc(2031, 5, 1);
            Notification[] notifications =
            [
                Notice(ana, NotificationKind.RoutineAssigned, ana, at, first),
                Notice(ana, NotificationKind.RoutineDueTomorrow, ana, at, first),
                Notice(teacher, NotificationKind.StudentFinishedRoutine, ana, at, first),
                Notice(ana, NotificationKind.RoutineAssigned, ana, at, second),
            ];
            db.RoutineAssignments.AddRange(first, second);
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();
            unassigned = first.Id;
            ids = notifications.Select(n => n.Id).ToArray();
        }

        // As the teacher's Unassign does, which leaves the notifications to the database.
        await using (var db = _fixture.CreateContext())
        {
            db.RoutineAssignments.Remove(await db.RoutineAssignments.SingleAsync(a => a.Id == unassigned));
            await db.SaveChangesAsync();
        }

        (await RemainingAsync(ids)).Should().Equal([ids[3]], "only the routine still assigned keeps its notifications");
    }

    [RealSqlFact]
    public async Task TheBellAndThePage_ReadAndMarkTheUsersNotifications_AndTheyAreExported()
    {
        await MigrateAsync();
        var clock = Clock(2031, 12, 23, 15);
        var teacher = NewUser("inbox-teacher");
        var ana = NewUser("inbox-ana", userName: $"inbox-ana-{Guid.NewGuid():N}");
        var anaLabel = $"{ana.UserName} ({ana.Email})";
        var dueAt = Utc(2031, 12, 24);
        Notification assigned, reminded, assignedAlone, joined, finished;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana);
            var classroom = NewClassroom(teacher, "SQL inbox classroom", ana);
            var toClass = new RoutineAssignment { Routine = await RoutineAsync(db, teacher, "SQL inbox routine"), Classroom = classroom, DueAt = dueAt };
            var toAna = new RoutineAssignment { Routine = await RoutineAsync(db, teacher, "SQL inbox solo routine"), StudentId = ana.Id };
            assigned = Notice(ana, NotificationKind.RoutineAssigned, ana, Utc(2031, 12, 19, 10), toClass);
            reminded = Notice(ana, NotificationKind.RoutineDueTomorrow, ana, Utc(2031, 12, 23, 12), toClass);
            assignedAlone = Notice(ana, NotificationKind.RoutineAssigned, ana, Utc(2031, 12, 22, 10), toAna);
            joined = Notice(teacher, NotificationKind.InviteAccepted, ana, Utc(2031, 12, 18, 10), classroom: classroom);
            finished = Notice(teacher, NotificationKind.StudentFinishedRoutine, ana, Utc(2031, 12, 21, 10), toClass, readAt: Utc(2031, 12, 21, 11));
            db.Notifications.AddRange(assigned, reminded, assignedAlone, joined, finished);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            var inbox = new NotificationInbox(db, clock);
            (await inbox.ListAsync(ana.Id)).Should().Equal(
                new NotificationItem(reminded.Id, NotificationKind.RoutineDueTomorrow, reminded.CreatedAt, false,
                    "SQL inbox routine", "SQL inbox classroom", dueAt, teacher.UserName, anaLabel),
                new NotificationItem(assignedAlone.Id, NotificationKind.RoutineAssigned, assignedAlone.CreatedAt, false,
                    "SQL inbox solo routine", null, null, teacher.UserName, anaLabel),
                new NotificationItem(assigned.Id, NotificationKind.RoutineAssigned, assigned.CreatedAt, false,
                    "SQL inbox routine", "SQL inbox classroom", dueAt, teacher.UserName, anaLabel));
            (await inbox.ListAsync(teacher.Id)).Should().Equal(
                new NotificationItem(finished.Id, NotificationKind.StudentFinishedRoutine, finished.CreatedAt, true,
                    "SQL inbox routine", "SQL inbox classroom", dueAt, teacher.UserName, anaLabel),
                new NotificationItem(joined.Id, NotificationKind.InviteAccepted, joined.CreatedAt, false,
                    null, "SQL inbox classroom", null, null, anaLabel));
            (await inbox.UnreadCountAsync(ana.Id)).Should().Be(3);
            (await inbox.UnreadCountAsync(teacher.Id)).Should().Be(1);

            (await inbox.OpenAsync(ana.Id, assignedAlone.Id)).Should().NotBeNull();
            (await inbox.OpenAsync(ana.Id, joined.Id)).Should().BeNull("it is her teacher's");
            (await inbox.UnreadCountAsync(ana.Id)).Should().Be(2);
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        await using (var db = _fixture.CreateContext())
        {
            var inbox = new NotificationInbox(db, clock);
            (await inbox.OpenAsync(ana.Id, assignedAlone.Id)).Should().NotBeNull();
            (await inbox.MarkAllReadAsync(ana.Id)).Should().Be(2);
            (await inbox.UnreadCountAsync(ana.Id)).Should().Be(0);
            (await inbox.UnreadCountAsync(teacher.Id)).Should().Be(1, "her teacher's are theirs to read");
        }

        var openedAt = Utc(2031, 12, 23, 15);
        var markedAt = Utc(2031, 12, 23, 15, 5);
        (await ExportedAsync(ana.Id)).Should().Equal(
            new Exported("RoutineAssigned", assigned.CreatedAt, markedAt, "SQL inbox routine", "SQL inbox classroom"),
            new Exported("RoutineAssigned", assignedAlone.CreatedAt, openedAt, "SQL inbox solo routine", null),
            new Exported("RoutineDueTomorrow", reminded.CreatedAt, markedAt, "SQL inbox routine", "SQL inbox classroom"));
        (await ExportedAsync(teacher.Id)).Should().Equal(
            new Exported("InviteAccepted", joined.CreatedAt, null, null, "SQL inbox classroom"),
            new Exported("StudentFinishedRoutine", finished.CreatedAt, finished.ReadAt, "SQL inbox routine", "SQL inbox classroom"));
    }

    [RealSqlFact]
    public async Task TheJob_DeletesOldNotifications_AndRemindsStudents_AsSoonAsItStarts()
    {
        await MigrateAsync();
        // No other test has a routine due on 6 November 2031. The job deletes every notification
        // from before 7 August 2031, other tests' too: none of them is left to check on them.
        var clock = Clock(2031, 11, 5, 12, 10);
        var teacher = NewUser("job-teacher");
        var ana = NewUser("job-ana");
        int dueId;
        await using (var db = _fixture.CreateContext())
        {
            db.Users.AddRange(teacher, ana);
            var routine = await RoutineAsync(db, teacher, "SQL job routine");
            var older = new RoutineAssignment { Routine = routine, StudentId = ana.Id, AssignedAt = Utc(2031, 7, 30) };
            var due = new RoutineAssignment { Routine = routine, StudentId = ana.Id, AssignedAt = Utc(2031, 11, 1), DueAt = Utc(2031, 11, 6) };
            db.RoutineAssignments.AddRange(older, due);
            db.Notifications.AddRange(
                Notice(ana, NotificationKind.RoutineAssigned, ana, Utc(2031, 8, 1), older),
                Notice(ana, NotificationKind.RoutineAssigned, ana, Utc(2031, 11, 1), due));
            await db.SaveChangesAsync();
            dueId = due.Id;
        }

        var notifierLog = new RecordingLogger<Notifier>();
        var jobLog = new RecordingLogger<NotificationsJob>();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped(_ => _fixture.CreateContext());
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddScoped<RoutineRounds>();
        services.AddScoped<Notifier>();
        services.AddSingleton<ILogger<Notifier>>(notifierLog);
        await using (var provider = services.BuildServiceProvider(validateScopes: true))
        using (var job = new NotificationsJob(provider.GetRequiredService<IServiceScopeFactory>(), clock, jobLog))
        {
            await job.StartAsync(CancellationToken.None);
            try
            {
                var waited = Stopwatch.StartNew();
                while (!jobLog.Entries.Any(e => e.Message.StartsWith("Reminded", StringComparison.Ordinal))
                    && jobLog.Problems().Count == 0
                    && waited.Elapsed < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                await job.StopAsync(CancellationToken.None);
            }
        }

        jobLog.Problems().Concat(notifierLog.Problems()).Should().BeEmpty();
        jobLog.Entries.Select(e => e.Message).Should().Contain("Reminded 1 students of the routines due tomorrow")
            .And.Contain(m => m.StartsWith("Deleted ", StringComparison.Ordinal), "the notification of 1 August is past 90 days");
        (await RowsAsync(n => n.StudentId == ana.Id)).Should().BeEquivalentTo(
        [
            new Row(ana.Id, NotificationKind.RoutineAssigned, ana.Id, dueId, Utc(2031, 11, 1)),
            new Row(ana.Id, NotificationKind.RoutineDueTomorrow, ana.Id, dueId, clock.GetUtcNow().UtcDateTime),
        ]);
    }

    private sealed record Row(string UserId, NotificationKind Kind, string StudentId, int? AssignmentId, DateTime CreatedAt);

    private sealed record Exported(string Kind, DateTime CreatedAt, DateTime? ReadAt, string? Routine, string? Classroom);

    private async Task MigrateAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
    }

    // Runs the work on several notifiers at once, each with its own context and connection, as
    // concurrent requests and replicas do.
    private async Task<int[]> RaceAsync(Func<Notifier, int, Task<int>> work, TimeProvider clock, ILogger<Notifier> log)
    {
        var contexts = new List<ApplicationDbContext>();
        try
        {
            for (var i = 0; i < Racers; i++)
            {
                var db = _fixture.CreateContext();
                contexts.Add(db);
                await db.Database.OpenConnectionAsync();
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var racers = contexts.Select((db, i) => Task.Run(async () =>
            {
                await start.Task;
                return await work(NewNotifier(db, clock, log), i);
            })).ToList();
            start.SetResult();
            return await Task.WhenAll(racers);
        }
        finally
        {
            foreach (var db in contexts)
            {
                await db.DisposeAsync();
            }
        }
    }

    private static Notifier NewNotifier(ApplicationDbContext db, TimeProvider clock, ILogger<Notifier> log)
        => new(db, new RoutineRounds(db, new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), clock), clock, log);

    private async Task<List<Row>> RowsAsync(Expression<Func<Notification, bool>> where)
    {
        await using var db = _fixture.CreateContext();
        return await db.Notifications.Where(where)
            .Select(n => new Row(n.UserId, n.Kind, n.StudentId, n.RoutineAssignmentId, n.CreatedAt))
            .ToListAsync();
    }

    private async Task<List<int>> RemainingAsync(int[] ids)
    {
        await using var db = _fixture.CreateContext();
        return await db.Notifications.Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync();
    }

    private async Task DeleteAsync(string userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        var result = await scope.ServiceProvider.GetRequiredService<PersonalDataService>().DeleteAccountAsync(user!);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    // The notifications in the user's export of their data.
    private async Task<List<Exported>> ExportedAsync(string userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        var export = await scope.ServiceProvider.GetRequiredService<PersonalDataService>().ExportAsync(user!);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(export));
        return json.RootElement.GetProperty("notifications").EnumerateArray().Select(n => new Exported(
            n.GetProperty("kind").GetString()!,
            n.GetProperty("createdAt").GetDateTime(),
            n.GetProperty("readAt").ValueKind == JsonValueKind.Null ? null : n.GetProperty("readAt").GetDateTime(),
            n.GetProperty("routine").GetString(),
            n.GetProperty("classroom").GetString())).ToList();
    }

    // A routine of one GuessNote question.
    private static async Task<Routine> RoutineAsync(ApplicationDbContext db, ApplicationUser teacher, string name)
    {
        var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
        return new Routine
        {
            Name = name,
            OwnerId = teacher.Id,
            Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 1 } }
        };
    }

    // The answer to the routine's one question, which finishes it; once the assignment is saved.
    private static ScoreSnapshot Answer(ApplicationUser student, RoutineAssignment assignment, Routine routine) => new()
    {
        UserId = student.Id,
        ExerciseId = routine.Items.Single().ExerciseId,
        IsCorrect = true,
        RoutineAssignmentId = assignment.Id,
        RoutineItemId = routine.Items.Single().Id,
        RoutineQuestion = 1
    };

    private static Classroom NewClassroom(ApplicationUser teacher, string name, params ApplicationUser[] members) => new()
    {
        Name = name,
        OwnerId = teacher.Id,
        Members = members.Select(m => new ClassroomMember { StudentId = m.Id }).ToList()
    };

    private static Notification Notice(ApplicationUser reader, NotificationKind kind, ApplicationUser student, DateTime createdAt,
        RoutineAssignment? assignment = null, Classroom? classroom = null, DateTime? readAt = null) => new()
    {
        UserId = reader.Id,
        Kind = kind,
        StudentId = student.Id,
        RoutineAssignment = assignment,
        Classroom = classroom,
        CreatedAt = createdAt,
        ReadAt = readAt
    };

    private static AnswerTimeTests.ManualClock Clock(int year, int month, int day, int hour, int minute = 0)
        => new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ApplicationUser NewUser(string prefix, string? userName = null)
    {
        var email = $"{prefix}.{Guid.NewGuid():N}@example.test";
        return new ApplicationUser { UserName = userName ?? email, Email = email, EmailConfirmed = true };
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries.ToArray();

        // What the tests fail on: warnings and errors.
        public List<string> Problems() => _entries
            .Where(e => e.Level >= LogLevel.Warning)
            .Select(e => $"{e.Level}: {e.Message} {e.Exception}")
            .ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
