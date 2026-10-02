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

    private static ApplicationDbContext Db(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private async Task<ApplicationUser> CreateUserAsync(string prefix, string firstName = "Test", string lastName = "User")
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
            EmailConfirmed = true
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
