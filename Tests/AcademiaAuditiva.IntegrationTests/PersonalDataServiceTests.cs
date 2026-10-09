using System.Text;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Covers the service behind Manage › Personal data. EF InMemory enforces
/// neither foreign keys nor database cascades, so these tests assert the
/// teaching rows the service removes itself; the practice and Identity rows
/// that SQL Server cascades from the user are not exercised here.
/// </summary>
public class PersonalDataServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ExerciseName = "PersonalDataTestExercise";
    private readonly TestWebApplicationFactory _factory;

    public PersonalDataServiceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteAccount_Student_RemovesTheirRowsAndKeepsTheTeachersClassroom()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student");

        Classroom classroom;
        Routine routine;
        RoutineItem item;
        RoutineAssignment classAssignment, studentAssignment;
        ClassroomInvite studentInvite, otherInvite;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            classroom = new Classroom { Name = "Theory 101", OwnerId = teacher.Id };
            routine = new Routine { Name = "Warm-up", OwnerId = teacher.Id };
            item = new RoutineItem { Routine = routine, ExerciseId = exerciseId, Order = 1 };
            db.AddRange(classroom, routine, item);
            await db.SaveChangesAsync();

            classAssignment = new RoutineAssignment { RoutineId = routine.Id, ClassroomId = classroom.Id };
            studentAssignment = new RoutineAssignment { RoutineId = routine.Id, StudentId = student.Id };
            studentInvite = NewInvite(classroom.Id, student.Email!, teacher.Id);
            otherInvite = NewInvite(classroom.Id, "someone.else@example.com", teacher.Id);
            db.AddRange(classAssignment, studentAssignment, studentInvite, otherInvite,
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id });
            await db.SaveChangesAsync();

            db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
            {
                RoutineAssignmentId = classAssignment.Id,
                StudentId = student.Id,
                RoutineItemId = item.Id,
                ExcludeItem = true
            });
            await db.SaveChangesAsync();
        }

        (await DeleteAsync(student.Id)).Succeeded.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            (await db.Users.AnyAsync(u => u.Id == student.Id)).Should().BeFalse();
            (await db.ClassroomMembers.AnyAsync(m => m.StudentId == student.Id)).Should().BeFalse();
            (await db.RoutineAssignments.AnyAsync(a => a.Id == studentAssignment.Id)).Should().BeFalse();
            (await db.RoutineAssignmentOverrides.AnyAsync(o => o.StudentId == student.Id)).Should().BeFalse();
            (await db.ClassroomInvites.AnyAsync(i => i.Id == studentInvite.Id)).Should().BeFalse();

            (await db.Users.AnyAsync(u => u.Id == teacher.Id)).Should().BeTrue();
            (await db.Classrooms.AnyAsync(c => c.Id == classroom.Id)).Should().BeTrue();
            (await db.Routines.AnyAsync(r => r.Id == routine.Id)).Should().BeTrue();
            (await db.RoutineItems.AnyAsync(i => i.Id == item.Id)).Should().BeTrue();
            (await db.RoutineAssignments.AnyAsync(a => a.Id == classAssignment.Id)).Should().BeTrue();
            (await db.ClassroomInvites.AnyAsync(i => i.Id == otherInvite.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task DeleteAccount_Teacher_RemovesTheirClassroomsAndRoutinesButKeepsStudents()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var colleague = await CreateUserAsync("colleague");
        var student = await CreateUserAsync("student");

        Classroom ownClassroom, colleagueClassroom;
        Routine ownRoutine, colleagueRoutine;
        RoutineItem ownItem, colleagueItem;
        RoutineAssignment ownInOwn, ownInColleague, colleagueInOwn;
        ClassroomMember colleagueMembership;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            ownClassroom = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            colleagueClassroom = new Classroom { Name = "Band", OwnerId = colleague.Id };
            ownRoutine = new Routine { Name = "Intervals", OwnerId = teacher.Id };
            colleagueRoutine = new Routine { Name = "Chords", OwnerId = colleague.Id };
            ownItem = new RoutineItem { Routine = ownRoutine, ExerciseId = exerciseId, Order = 1 };
            colleagueItem = new RoutineItem { Routine = colleagueRoutine, ExerciseId = exerciseId, Order = 1 };
            db.AddRange(ownClassroom, colleagueClassroom, ownRoutine, colleagueRoutine, ownItem, colleagueItem);
            await db.SaveChangesAsync();

            ownInOwn = new RoutineAssignment { RoutineId = ownRoutine.Id, ClassroomId = ownClassroom.Id };
            // Routines can be assigned across owners; both directions must go.
            ownInColleague = new RoutineAssignment { RoutineId = ownRoutine.Id, ClassroomId = colleagueClassroom.Id };
            colleagueInOwn = new RoutineAssignment { RoutineId = colleagueRoutine.Id, ClassroomId = ownClassroom.Id };
            colleagueMembership = new ClassroomMember { ClassroomId = colleagueClassroom.Id, StudentId = student.Id };
            db.AddRange(ownInOwn, ownInColleague, colleagueInOwn, colleagueMembership,
                new ClassroomMember { ClassroomId = ownClassroom.Id, StudentId = student.Id },
                NewInvite(ownClassroom.Id, "pending@example.com", teacher.Id));
            await db.SaveChangesAsync();

            db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
            {
                RoutineAssignmentId = ownInColleague.Id,
                StudentId = student.Id,
                RoutineItemId = ownItem.Id,
                OverrideTargetCount = 5
            });
            await db.SaveChangesAsync();
        }

        (await DeleteAsync(teacher.Id)).Succeeded.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var removedAssignments = new[] { ownInOwn.Id, ownInColleague.Id, colleagueInOwn.Id };
            (await db.Users.AnyAsync(u => u.Id == teacher.Id)).Should().BeFalse();
            (await db.Classrooms.AnyAsync(c => c.OwnerId == teacher.Id)).Should().BeFalse();
            (await db.Routines.AnyAsync(r => r.OwnerId == teacher.Id)).Should().BeFalse();
            (await db.RoutineItems.AnyAsync(i => i.RoutineId == ownRoutine.Id)).Should().BeFalse();
            (await db.RoutineAssignments.AnyAsync(a => removedAssignments.Contains(a.Id))).Should().BeFalse();
            (await db.RoutineAssignmentOverrides.AnyAsync(o => o.RoutineItemId == ownItem.Id)).Should().BeFalse();
            (await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == ownClassroom.Id)).Should().BeFalse();
            (await db.ClassroomInvites.AnyAsync(i => i.ClassroomId == ownClassroom.Id)).Should().BeFalse();

            (await db.Users.AnyAsync(u => u.Id == student.Id)).Should().BeTrue();
            (await db.Classrooms.AnyAsync(c => c.Id == colleagueClassroom.Id)).Should().BeTrue();
            (await db.ClassroomMembers.AnyAsync(m => m.Id == colleagueMembership.Id)).Should().BeTrue();
            (await db.Routines.AnyAsync(r => r.Id == colleagueRoutine.Id)).Should().BeTrue();
            (await db.RoutineItems.AnyAsync(i => i.Id == colleagueItem.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task OwnsTeachingData_IsTrueOnlyForUsersWithClassroomsOrRoutines()
    {
        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            db.Routines.Add(new Routine { Name = "Scales", OwnerId = teacher.Id });
            await db.SaveChangesAsync();
        }

        using var check = _factory.Services.CreateScope();
        var service = check.ServiceProvider.GetRequiredService<PersonalDataService>();
        (await service.OwnsTeachingDataAsync(teacher.Id)).Should().BeTrue();
        (await service.OwnsTeachingDataAsync(student.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Export_IncludesProfilePracticeAndClassroomsButOnlyCountsOtherPeople()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student", "Élodie", "Tremblay");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var classroom = new Classroom { Name = "Chorale 5e année", OwnerId = teacher.Id };
            db.Classrooms.Add(classroom);
            await db.SaveChangesAsync();
            db.AddRange(
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id },
                NewInvite(classroom.Id, student.Email!, teacher.Id),
                new ScoreAggregate { UserId = student.Id, ExerciseId = exerciseId, CorrectCount = 3, ErrorCount = 1, BestScore = 3 },
                new UserTutorial { UserId = student.Id, TutorialKey = "Dashboard", SeenAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), Finished = true });
            await db.SaveChangesAsync();

            var run = new GameRun
            {
                UserId = student.Id, Mode = "survival", ExerciseId = exerciseId, FilterJson = """{"level":"2"}""",
                StartedAt = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), Score = 1, Answered = 2,
            };
            db.GameRuns.Add(run);
            await db.SaveChangesAsync();
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = student.Id, ExerciseId = exerciseId, IsCorrect = true, GameRunId = run.Id,
                Timestamp = new DateTime(2026, 10, 2, 12, 0, 5, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        var studentJson = await ExportAsync(student.Id);
        studentJson.Should().Contain("Élodie", "accented names must stay readable in the export");
        using (var doc = JsonDocument.Parse(studentJson))
        {
            var root = doc.RootElement;
            root.GetProperty("profile").GetProperty("FirstName").GetString().Should().Be("Élodie");
            root.GetProperty("profile").GetProperty("Email").GetString().Should().Be(student.Email);

            var totals = root.GetProperty("practice").GetProperty("totals");
            totals.GetArrayLength().Should().Be(1);
            totals[0].GetProperty("exercise").GetString().Should().Be(ExerciseName);
            totals[0].GetProperty("correctCount").GetInt32().Should().Be(3);

            var games = root.GetProperty("games");
            games.GetArrayLength().Should().Be(1);
            games[0].GetProperty("mode").GetString().Should().Be("survival");
            games[0].GetProperty("exercise").GetString().Should().Be(ExerciseName);
            games[0].GetProperty("score").GetInt32().Should().Be(1);
            var answers = root.GetProperty("practice").GetProperty("answers");
            answers.GetArrayLength().Should().Be(1);
            answers[0].GetProperty("gameRunId").GetInt32().Should().Be(games[0].GetProperty("id").GetInt32());

            var tutorials = root.GetProperty("tutorials");
            tutorials.GetArrayLength().Should().Be(1);
            tutorials[0].GetProperty("tutorial").GetString().Should().Be("Dashboard");
            tutorials[0].GetProperty("finished").GetBoolean().Should().BeTrue();

            var studentSection = root.GetProperty("student");
            studentSection.GetProperty("classrooms")[0].GetProperty("classroom").GetString().Should().Be("Chorale 5e année");
            studentSection.GetProperty("invitations").GetArrayLength().Should().Be(1);
        }

        var teacherJson = await ExportAsync(teacher.Id);
        teacherJson.Should().NotContain(student.Id).And.NotContain(student.Email!.ToLowerInvariant()).And.NotContain("Élodie");
        using (var doc = JsonDocument.Parse(teacherJson))
        {
            var ownedClassroom = doc.RootElement.GetProperty("teacher").GetProperty("classrooms")[0];
            ownedClassroom.GetProperty("students").GetInt32().Should().Be(1);
            ownedClassroom.GetProperty("invitations").GetInt32().Should().Be(1);
        }
    }

    [Fact]
    public async Task Export_IncludesEveryAudioExercisesMetadata_AndDistinguishesItsTrackFromMusic()
    {
        var student = await CreateUserAsync("audio-export");
        var filters = new Dictionary<string, string>
        {
            ["GuessNote"] = """{"keySelect":"C4"}""",
            ["LevelMatch"] = """{"lmLevel":"intermediate","lmDifferenceDb":"4","lmSourceKind":"drums"}""",
            ["StereoPosition"] = """{"spLevel":"advanced","spPosition":"-0.25","spSourceKind":"keys"}""",
            ["GuessFrequency"] = """{"gfLevel":"advanced","gfFrequencyHz":"1000","gfSourceKind":"keys"}""",
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            SeedData.SeedExercises(db);
            foreach (var (name, json) in filters)
            {
                var id = db.Exercises.Single(e => e.Name == name).ExerciseId;
                db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = student.Id, ExerciseId = id, IsCorrect = true, FilterJson = json });
                db.ScoreAggregates.Add(new ScoreAggregate { UserId = student.Id, ExerciseId = id, CorrectCount = 1 });
            }
            await db.SaveChangesAsync();
        }

        using var export = JsonDocument.Parse(await ExportAsync(student.Id));
        var practice = export.RootElement.GetProperty("practice");
        foreach (var section in new[] { "totals", "answers" })
        {
            var rows = practice.GetProperty(section).EnumerateArray().ToArray();
            rows.Should().HaveCount(4);
            foreach (var row in rows)
            {
                var name = row.GetProperty("exercise").GetString()!;
                row.GetProperty("track").GetString().Should().Be(name == "GuessNote" ? "Music" : "Audio");
                if (section == "answers") row.GetProperty("filterJson").GetString().Should().Be(filters[name]);
            }
        }
    }

    [Fact]
    public async Task DeleteAccount_RemovesTheStudentsTicks_AndEveryTickOfTheTeachersAssignments()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var colleague = await CreateUserAsync("colleague");
        var student = await CreateUserAsync("student");
        var classmate = await CreateUserAsync("classmate");

        RoutineAssignment own, colleagues;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var choir = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            var band = new Classroom { Name = "Band", OwnerId = colleague.Id };
            var intervals = new Routine { Name = "Intervals", OwnerId = teacher.Id };
            var chords = new Routine { Name = "Chords", OwnerId = colleague.Id };
            db.AddRange(choir, band, intervals, chords,
                new RoutineItem { Routine = intervals, ExerciseId = exerciseId, Order = 1 },
                new RoutineItem { Routine = chords, ExerciseId = exerciseId, Order = 1 });
            await db.SaveChangesAsync();

            own = ChosenOnly(intervals.Id, choir.Id, student, classmate);
            colleagues = ChosenOnly(chords.Id, band.Id, student, classmate);
            db.AddRange(own, colleagues,
                new ClassroomMember { ClassroomId = choir.Id, StudentId = student.Id },
                new ClassroomMember { ClassroomId = choir.Id, StudentId = classmate.Id },
                new ClassroomMember { ClassroomId = band.Id, StudentId = student.Id },
                new ClassroomMember { ClassroomId = band.Id, StudentId = classmate.Id });
            await db.SaveChangesAsync();
        }

        (await DeleteAsync(student.Id)).Succeeded.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            (await db.RoutineAssignmentStudents.AnyAsync(s => s.StudentId == student.Id)).Should().BeFalse();
            (await ChosenAsync(db, own.Id)).Should().Equal(classmate.Id);
            (await ChosenAsync(db, colleagues.Id)).Should().Equal(classmate.Id);
        }

        (await DeleteAsync(teacher.Id)).Succeeded.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            (await db.RoutineAssignments.AnyAsync(a => a.Id == own.Id)).Should().BeFalse();
            (await ChosenAsync(db, own.Id)).Should().BeEmpty("the ticks go with the assignment");
            (await ChosenAsync(db, colleagues.Id)).Should().Equal(classmate.Id);
        }
    }

    [Fact]
    public async Task Export_IncludesTheEmailSettings_AndOnlyTheRoutinesGivenToTheStudent()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student", language: "pt-BR", routineEmailsOff: true);
        var classmate = await CreateUserAsync("classmate");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var classroom = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            var routines = new[] { "Whole class", "Ticked", "Not ticked" }
                .Select(name => new Routine { Name = name, OwnerId = teacher.Id }).ToArray();
            db.Add(classroom);
            db.AddRange(routines);
            db.AddRange(routines.Select(r => new RoutineItem { Routine = r, ExerciseId = exerciseId, Order = 1 }));
            await db.SaveChangesAsync();

            var assignments = new[]
            {
                new RoutineAssignment { RoutineId = routines[0].Id, ClassroomId = classroom.Id },
                ChosenOnly(routines[1].Id, classroom.Id, student, classmate),
                ChosenOnly(routines[2].Id, classroom.Id, classmate),
            };
            for (var i = 0; i < assignments.Length; i++)
            {
                assignments[i].AssignedAt = new DateTime(2026, 10, 1 + i, 12, 0, 0, DateTimeKind.Utc);
            }
            db.AddRange(assignments);
            db.AddRange(
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id },
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = classmate.Id });
            await db.SaveChangesAsync();
        }

        using var doc = JsonDocument.Parse(await ExportAsync(student.Id));
        var profile = doc.RootElement.GetProperty("profile");
        profile.GetProperty("Language").GetString().Should().Be("pt-BR");
        profile.GetProperty("RoutineEmailsOff").GetString().Should().Be("True");
        doc.RootElement.GetProperty("student").GetProperty("assignedRoutines").EnumerateArray()
            .Select(a => a.GetProperty("routine").GetString())
            .Should().Equal(["Whole class", "Ticked"], "a routine the teacher gave only to other students isn't the student's");
    }

    [Fact]
    public async Task Export_IncludesTheUsersNotifications_ButNotTheStudentsTheyAreAbout()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student");
        var classmate = await CreateUserAsync("classmate");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var choir = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            var intervals = new Routine { Name = "Intervals", OwnerId = teacher.Id };
            var solo = new Routine { Name = "Solo", OwnerId = teacher.Id };
            var toClass = new RoutineAssignment { Routine = intervals, Classroom = choir };
            var toStudent = new RoutineAssignment { Routine = solo, StudentId = student.Id };
            db.AddRange(choir, intervals, solo, toClass, toStudent,
                new RoutineItem { Routine = intervals, ExerciseId = exerciseId, Order = 1 },
                new RoutineItem { Routine = solo, ExerciseId = exerciseId, Order = 1 },
                new ClassroomMember { Classroom = choir, StudentId = student.Id },
                new ClassroomMember { Classroom = choir, StudentId = classmate.Id });
            // Newest first, to be listed oldest first.
            db.Notifications.AddRange(
                Notice(student, NotificationKind.RoutineDueTomorrow, student, Utc(2026, 10, 5, 12), toStudent),
                Notice(student, NotificationKind.RoutineAssigned, student, Utc(2026, 10, 3, 9), toClass, readAt: Utc(2026, 10, 3, 10)),
                Notice(teacher, NotificationKind.StudentFinishedRoutine, student, Utc(2026, 10, 6, 18), toClass),
                Notice(teacher, NotificationKind.InviteAccepted, classmate, Utc(2026, 10, 2, 8), classroom: choir),
                Notice(classmate, NotificationKind.RoutineAssigned, classmate, Utc(2026, 10, 3, 9), toClass));
            await db.SaveChangesAsync();
        }

        using (var doc = JsonDocument.Parse(await ExportAsync(student.Id)))
        {
            var notifications = doc.RootElement.GetProperty("notifications");
            foreach (var notification in notifications.EnumerateArray())
            {
                notification.EnumerateObject().Select(p => p.Name).Should().Equal("kind", "createdAt", "readAt", "routine", "classroom");
            }
            Notices(notifications).Should().Equal(
                ("RoutineAssigned", Utc(2026, 10, 3, 9), Utc(2026, 10, 3, 10), "Intervals", "Choir"),
                ("RoutineDueTomorrow", Utc(2026, 10, 5, 12), null, "Solo", null));
        }

        var teacherJson = await ExportAsync(teacher.Id);
        foreach (var someone in new[] { student, classmate })
        {
            teacherJson.Should().NotContain(someone.Id).And.NotContainEquivalentOf(someone.Email!,
                "the teacher's notifications name the routine or the classroom, not the student");
        }
        using (var doc = JsonDocument.Parse(teacherJson))
        {
            Notices(doc.RootElement.GetProperty("notifications")).Should().Equal(
                ("InviteAccepted", Utc(2026, 10, 2, 8), null, null, "Choir"),
                ("StudentFinishedRoutine", Utc(2026, 10, 6, 18), null, "Intervals", "Choir"));
        }
    }

    [Fact]
    public async Task DeleteAccount_RemovesTheUsersNotifications_AndThoseAboutThem()
    {
        var exerciseId = await EnsureExerciseAsync();
        var teacher = await CreateUserAsync("teacher");
        var colleague = await CreateUserAsync("colleague");
        var student = await CreateUserAsync("student");
        var classmate = await CreateUserAsync("classmate");
        var at = Utc(2026, 10, 5, 12);

        Notification[] notifications;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var choir = new Classroom { Name = "Choir", OwnerId = teacher.Id };
            var band = new Classroom { Name = "Band", OwnerId = colleague.Id };
            var intervals = new Routine { Name = "Intervals", OwnerId = teacher.Id };
            var chords = new Routine { Name = "Chords", OwnerId = colleague.Id };
            var own = new RoutineAssignment { Routine = intervals, Classroom = choir };
            var colleagues = new RoutineAssignment { Routine = chords, Classroom = band };
            db.AddRange(choir, band, intervals, chords, own, colleagues,
                new RoutineItem { Routine = intervals, ExerciseId = exerciseId, Order = 1 },
                new RoutineItem { Routine = chords, ExerciseId = exerciseId, Order = 1 });
            foreach (var classroom in new[] { choir, band })
            {
                db.AddRange(new[] { student, classmate }.Select(s => new ClassroomMember { Classroom = classroom, StudentId = s.Id }));
            }
            notifications =
            [
                Notice(student, NotificationKind.RoutineAssigned, student, at, own),
                Notice(classmate, NotificationKind.RoutineAssigned, classmate, at, own),
                Notice(teacher, NotificationKind.StudentFinishedRoutine, student, at, own),
                Notice(teacher, NotificationKind.InviteAccepted, classmate, at, classroom: choir),
                Notice(colleague, NotificationKind.StudentFinishedRoutine, student, at, colleagues),
                Notice(colleague, NotificationKind.InviteAccepted, classmate, at, classroom: band),
                Notice(classmate, NotificationKind.RoutineAssigned, classmate, at, colleagues),
            ];
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();
        }

        (await DeleteAsync(student.Id)).Succeeded.Should().BeTrue();
        (await RemainingAsync(notifications)).Should().BeEquivalentTo(
            [notifications[1].Id, notifications[3].Id, notifications[5].Id, notifications[6].Id],
            "the student's own notifications go, and those their teachers had about them");

        (await DeleteAsync(teacher.Id)).Succeeded.Should().BeTrue();
        (await RemainingAsync(notifications)).Should().BeEquivalentTo(
            [notifications[5].Id, notifications[6].Id],
            "the teacher's own go, and those about the assignments and classrooms removed with the teacher");
    }

    private static RoutineAssignment ChosenOnly(int routineId, int classroomId, params ApplicationUser[] students) => new()
    {
        RoutineId = routineId,
        ClassroomId = classroomId,
        ChosenStudentsOnly = true,
        ChosenStudents = students.Select(s => new RoutineAssignmentStudent { StudentId = s.Id }).ToList(),
    };

    private static Task<List<string>> ChosenAsync(ApplicationDbContext db, int assignmentId) =>
        db.RoutineAssignmentStudents.Where(s => s.RoutineAssignmentId == assignmentId).Select(s => s.StudentId).ToListAsync();

    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static Notification Notice(ApplicationUser reader, NotificationKind kind, ApplicationUser student, DateTime createdAt,
        RoutineAssignment? assignment = null, Classroom? classroom = null, DateTime? readAt = null) => new()
    {
        UserId = reader.Id,
        Kind = kind,
        StudentId = student.Id,
        RoutineAssignment = assignment,
        Classroom = classroom,
        CreatedAt = createdAt,
        ReadAt = readAt,
    };

    private static List<(string? Kind, DateTime CreatedAt, DateTime? ReadAt, string? Routine, string? Classroom)> Notices(JsonElement notifications) =>
        notifications.EnumerateArray()
            .Select(n => (
                n.GetProperty("kind").GetString(),
                n.GetProperty("createdAt").GetDateTime(),
                n.GetProperty("readAt").ValueKind == JsonValueKind.Null ? (DateTime?)null : n.GetProperty("readAt").GetDateTime(),
                n.GetProperty("routine").GetString(),
                n.GetProperty("classroom").GetString()))
            .ToList();

    private async Task<List<int>> RemainingAsync(IEnumerable<Notification> notifications)
    {
        var ids = notifications.Select(n => n.Id).ToList();
        using var scope = _factory.Services.CreateScope();
        return await Db(scope).Notifications.Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync();
    }

    private static ApplicationDbContext Db(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private async Task<ApplicationUser> CreateUserAsync(string prefix, string firstName = "Test", string lastName = "User",
        string? language = null, bool routineEmailsOff = false)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // Mixed case on purpose: invites are stored trimmed and lower-cased.
        var email = $"{prefix}.{Guid.NewGuid():N}@Example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
            Language = language,
            RoutineEmailsOff = routineEmailsOff
        };
        var result = await users.CreateAsync(user);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<int> EnsureExerciseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var existing = await db.Exercises.FirstOrDefaultAsync(e => e.Name == ExerciseName);
        if (existing != null) return existing.ExerciseId;

        var exercise = new Exercise
        {
            Name = ExerciseName,
            Description = "Test exercise",
            ExerciseType = new ExerciseType { Name = "Test", DisplayName = "Test" },
            ExerciseCategory = new ExerciseCategory { Name = "Test", DisplayName = "Test" },
            DifficultyLevel = new DifficultyLevel { Name = "Test", DisplayName = "Test" }
        };
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync();
        return exercise.ExerciseId;
    }

    private static ClassroomInvite NewInvite(int classroomId, string email, string createdById) => new()
    {
        ClassroomId = classroomId,
        Email = email.Trim().ToLowerInvariant(),
        Token = Guid.NewGuid().ToString("N"),
        ExpiresAt = DateTime.UtcNow.AddDays(14),
        CreatedById = createdById
    };

    private async Task<IdentityResult> DeleteAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        return await scope.ServiceProvider.GetRequiredService<PersonalDataService>().DeleteAccountAsync(user!);
    }

    private async Task<string> ExportAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        var bytes = await scope.ServiceProvider.GetRequiredService<PersonalDataService>().ExportAsync(user!);
        return Encoding.UTF8.GetString(bytes);
    }
}
