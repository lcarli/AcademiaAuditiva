using System.Text;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Scoring;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

[Collection(RealSqlServerCollection.Name)]
public sealed class RealSqlServerTests
{
    private readonly RealSqlServerFixture _fixture;

    public RealSqlServerTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task Migrations_ApplyToEmptyDatabase_AndModelHasNoPendingChanges()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "the EF model must match the latest migration snapshot before production startup calls Migrate()");
        (await db.Database.SqlQueryRaw<int>("SELECT 1 AS [Value]").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'AppCache'").SingleAsync())
            .Should().Be(1, "the SQL Server distributed-cache table is created by migrations");
    }

    [RealSqlFact]
    public async Task SeedExercises_IsIdempotent_WithRealUniqueConstraints()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        SeedData.SeedExercises(db);
        var first = await CountsAsync(db);
        SeedData.SeedExercises(db);
        var second = await CountsAsync(db);

        second.Should().Be(first);
    }

    [RealSqlFact]
    public async Task ScoreAggregate_Upsert_UpdatesOneRowPerUserAndExercise()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);
        var user = new ApplicationUser { UserName = UniqueEmail("score"), Email = UniqueEmail("score"), EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var exerciseId = await db.Exercises.Select(e => e.ExerciseId).FirstAsync();

        await UpsertScoreAsync(db, user.Id, exerciseId, isCorrect: true);
        await UpsertScoreAsync(db, user.Id, exerciseId, isCorrect: false);

        var aggregate = await db.ScoreAggregates.SingleAsync(a => a.UserId == user.Id && a.ExerciseId == exerciseId);
        aggregate.CorrectCount.Should().Be(1);
        aggregate.ErrorCount.Should().Be(1);
        aggregate.BestScore.Should().Be(1);
        (await db.ScoreAggregates.CountAsync(a => a.UserId == user.Id && a.ExerciseId == exerciseId)).Should().Be(1);
    }

    [RealSqlFact]
    public async Task PersonalDataDeletion_RemovesPracticeAndTeachingRows_WithRealForeignKeys()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            SeedData.SeedExercises(db);
        }

        var teacher = await CreateUserAsync("teacher");
        var student = await CreateUserAsync("student");
        int classroomId, routineId, assignmentId, exerciseId;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            exerciseId = await db.Exercises.Select(e => e.ExerciseId).FirstAsync();
            var classroom = new Classroom { Name = "SQL FK classroom", OwnerId = teacher.Id };
            var routine = new Routine { Name = "SQL FK routine", OwnerId = teacher.Id };
            var item = new RoutineItem { Routine = routine, ExerciseId = exerciseId, Order = 1 };
            db.AddRange(classroom, routine, item);
            await db.SaveChangesAsync();
            classroomId = classroom.Id;
            routineId = routine.Id;

            var assignment = new RoutineAssignment { RoutineId = routine.Id, StudentId = student.Id };
            db.AddRange(
                assignment,
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id },
                new ClassroomInvite { ClassroomId = classroom.Id, Email = student.Email!.ToLowerInvariant(), CreatedById = teacher.Id, Token = Guid.NewGuid().ToString("N") },
                new Score { UserId = student.Id, ExerciseId = exerciseId, CorrectCount = 1, ErrorCount = 0, BestScore = 1, Timestamp = DateTime.UtcNow },
                new ScoreSnapshot { UserId = student.Id, ExerciseId = exerciseId, IsCorrect = true, TimeSpentSeconds = 3 },
                new ScoreAggregate { UserId = student.Id, ExerciseId = exerciseId, CorrectCount = 1, ErrorCount = 0, BestScore = 1, LastAttemptAt = DateTime.UtcNow },
                new UserTutorial { UserId = student.Id, TutorialKey = "Dashboard", Finished = true });
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var service = scope.ServiceProvider.GetRequiredService<PersonalDataService>();
            var user = await users.FindByIdAsync(student.Id);
            user.Should().NotBeNull();

            var result = await service.DeleteAccountAsync(user!);

            result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await using (var db = _fixture.CreateContext())
        {
            (await db.Users.AnyAsync(u => u.Id == student.Id)).Should().BeFalse();
            (await db.Scores.AnyAsync(s => s.UserId == student.Id)).Should().BeFalse();
            (await db.ScoreSnapshots.AnyAsync(s => s.UserId == student.Id)).Should().BeFalse();
            (await db.ScoreAggregates.AnyAsync(s => s.UserId == student.Id)).Should().BeFalse();
            (await db.UserTutorials.AnyAsync(t => t.UserId == student.Id)).Should().BeFalse();
            (await db.ClassroomMembers.AnyAsync(m => m.StudentId == student.Id)).Should().BeFalse();
            (await db.RoutineAssignments.AnyAsync(a => a.Id == assignmentId)).Should().BeFalse();

            (await db.Users.AnyAsync(u => u.Id == teacher.Id)).Should().BeTrue();
            (await db.Classrooms.AnyAsync(c => c.Id == classroomId)).Should().BeTrue();
            (await db.Routines.AnyAsync(r => r.Id == routineId)).Should().BeTrue();
        }
    }

    [RealSqlFact]
    public async Task AudioPersonalData_ExportsTrustedFiltersAndTrack_ThenDeletesOnlyTheOwnersPractice()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            SeedData.SeedExercises(db);
        }
        var owner = await CreateUserAsync("audio-owner");
        var other = await CreateUserAsync("audio-other");
        const string metadata = """{"gfLevel":"advanced","gfFrequencyHz":"1000","gfSourceKind":"keys"}""";
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = await db.Exercises.ToDictionaryAsync(e => e.Name, e => e.ExerciseId);
            foreach (var name in new[] { "GuessNote", "LevelMatch", "StereoPosition", "GuessFrequency" })
            {
                db.ScoreSnapshots.Add(new ScoreSnapshot
                {
                    UserId = owner.Id, ExerciseId = ids[name], IsCorrect = true,
                    FilterJson = name == "GuessFrequency" ? metadata : null,
                });
                db.ScoreAggregates.Add(new ScoreAggregate { UserId = owner.Id, ExerciseId = ids[name], CorrectCount = 1 });
            }
            db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = other.Id, ExerciseId = ids["GuessFrequency"], FilterJson = metadata });
            db.ScoreAggregates.Add(new ScoreAggregate { UserId = other.Id, ExerciseId = ids["GuessFrequency"], ErrorCount = 1 });
            await db.SaveChangesAsync();
        }
        using (var export = JsonDocument.Parse(await ExportAsync(owner.Id)))
        {
            var answers = export.RootElement.GetProperty("practice").GetProperty("answers").EnumerateArray().ToArray();
            answers.Should().HaveCount(4);
            answers.Single(a => a.GetProperty("exercise").GetString() == "GuessFrequency")
                .GetProperty("filterJson").GetString().Should().Be(metadata);
            foreach (var answer in answers)
                answer.GetProperty("track").GetString().Should().Be(
                    answer.GetProperty("exercise").GetString() == "GuessNote" ? "Music" : "Audio");
        }
        using (var scope = _fixture.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PersonalDataService>();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(owner.Id);
            (await service.DeleteAccountAsync(user!)).Succeeded.Should().BeTrue();
        }
        await using var check = _fixture.CreateContext();
        (await check.ScoreSnapshots.AnyAsync(s => s.UserId == owner.Id)).Should().BeFalse();
        (await check.ScoreAggregates.AnyAsync(s => s.UserId == owner.Id)).Should().BeFalse();
        (await check.ScoreSnapshots.CountAsync(s => s.UserId == other.Id)).Should().Be(1);
        (await check.ScoreAggregates.CountAsync(s => s.UserId == other.Id)).Should().Be(1);
    }

    [RealSqlFact]
    public async Task PersonalDataDeletion_IsAllOrNothing_WhenTheCommitFails()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            SeedData.SeedExercises(db);
        }

        var teacher = await CreateUserAsync("atomic-teacher");
        var student = await CreateUserAsync("atomic-student");
        int classroomId, routineId, itemId, assignmentId;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exerciseId = await db.Exercises.Select(e => e.ExerciseId).FirstAsync();
            var classroom = new Classroom { Name = "Atomic classroom", OwnerId = teacher.Id };
            var routine = new Routine { Name = "Atomic routine", OwnerId = teacher.Id };
            var item = new RoutineItem { Routine = routine, ExerciseId = exerciseId, Order = 1 };
            db.AddRange(classroom, routine, item);
            await db.SaveChangesAsync();

            var assignment = new RoutineAssignment { RoutineId = routine.Id, ClassroomId = classroom.Id };
            db.AddRange(
                assignment,
                new ClassroomMember { ClassroomId = classroom.Id, StudentId = student.Id },
                new ClassroomInvite { ClassroomId = classroom.Id, Email = "atomic-invitee@example.test", CreatedById = teacher.Id, Token = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            (classroomId, routineId, itemId, assignmentId) = (classroom.Id, routine.Id, item.Id, assignment.Id);
        }

        _fixture.CommitFailure.Arm();
        try
        {
            using var scope = _fixture.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var service = scope.ServiceProvider.GetRequiredService<PersonalDataService>();
            var user = await users.FindByIdAsync(teacher.Id);
            user.Should().NotBeNull();

            var delete = () => service.DeleteAccountAsync(user!);

            await delete.Should().ThrowAsync<InvalidOperationException>(
                    "the user row must be deleted by the same transaction as the teaching rows")
                .WithMessage(FailCommitAfterUserDelete.Message);
        }
        finally
        {
            _fixture.CommitFailure.Disarm();
        }

        await using (var db = _fixture.CreateContext())
        {
            (await db.Users.AnyAsync(u => u.Id == teacher.Id)).Should().BeTrue();
            (await db.Classrooms.AnyAsync(c => c.Id == classroomId)).Should().BeTrue("its delete was rolled back with the user delete");
            (await db.Routines.AnyAsync(r => r.Id == routineId)).Should().BeTrue();
            (await db.RoutineItems.AnyAsync(i => i.Id == itemId)).Should().BeTrue();
            (await db.RoutineAssignments.AnyAsync(a => a.Id == assignmentId)).Should().BeTrue();
            (await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classroomId && m.StudentId == student.Id)).Should().BeTrue();
            (await db.ClassroomInvites.AnyAsync(i => i.ClassroomId == classroomId)).Should().BeTrue();
        }
    }

    [RealSqlFact]
    public async Task PersonalDataDeletion_RemovesTheChosenStudentRows_WithRealForeignKeys()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            SeedData.SeedExercises(db);
        }

        var teacher = await CreateUserAsync("chooser-teacher");
        var student = await CreateUserAsync("chosen-student");
        var classmate = await CreateUserAsync("chosen-classmate");
        int assignmentId;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exerciseId = await db.Exercises.Select(e => e.ExerciseId).FirstAsync();
            var assignment = new RoutineAssignment
            {
                Routine = new Routine
                {
                    Name = "SQL chosen routine",
                    OwnerId = teacher.Id,
                    Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1 } }
                },
                Classroom = new Classroom
                {
                    Name = "SQL chosen classroom",
                    OwnerId = teacher.Id,
                    Members = { new ClassroomMember { StudentId = student.Id }, new ClassroomMember { StudentId = classmate.Id } }
                },
                ChosenStudentsOnly = true,
                ChosenStudents =
                {
                    new RoutineAssignmentStudent { StudentId = student.Id },
                    new RoutineAssignmentStudent { StudentId = classmate.Id }
                }
            };
            db.RoutineAssignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        using (var export = JsonDocument.Parse(await ExportAsync(student.Id)))
        {
            export.RootElement.GetProperty("student").GetProperty("assignedRoutines").EnumerateArray()
                .Select(a => a.GetProperty("routine").GetString()).Should().Equal("SQL chosen routine");
        }

        // The student's row restricts the deletion of their account: the service removes it first.
        await DeleteAsync(student.Id);

        await using (var db = _fixture.CreateContext())
        {
            (await db.Users.AnyAsync(u => u.Id == student.Id)).Should().BeFalse();
            (await db.RoutineAssignmentStudents.Where(s => s.RoutineAssignmentId == assignmentId).Select(s => s.StudentId).ToListAsync())
                .Should().Equal([classmate.Id], "the classmate keeps the routine");
        }

        await DeleteAsync(teacher.Id);

        await using (var db = _fixture.CreateContext())
        {
            (await db.RoutineAssignments.AnyAsync(a => a.Id == assignmentId)).Should().BeFalse();
            (await db.RoutineAssignmentStudents.AnyAsync(s => s.RoutineAssignmentId == assignmentId)).Should().BeFalse();
            (await db.Users.AnyAsync(u => u.Id == classmate.Id)).Should().BeTrue();
        }
    }

    private async Task DeleteAsync(string userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        var result = await scope.ServiceProvider.GetRequiredService<PersonalDataService>().DeleteAccountAsync(user!);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task<string> ExportAsync(string userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user.Should().NotBeNull();
        return Encoding.UTF8.GetString(await scope.ServiceProvider.GetRequiredService<PersonalDataService>().ExportAsync(user!));
    }

    private async Task<ApplicationUser> CreateUserAsync(string prefix)
    {
        using var scope = _fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = UniqueEmail(prefix);
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static async Task<(int Exercises, int Types, int Categories, int Difficulties, int Badges)> CountsAsync(ApplicationDbContext db)
        => (await db.Exercises.CountAsync(),
            await db.ExerciseTypes.CountAsync(),
            await db.ExerciseCategories.CountAsync(),
            await db.DifficultyLevels.CountAsync(),
            await db.Badges.CountAsync());

    private static async Task UpsertScoreAsync(ApplicationDbContext db, string userId, int exerciseId, bool isCorrect)
    {
        var aggregate = await db.ScoreAggregates.FirstOrDefaultAsync(a => a.UserId == userId && a.ExerciseId == exerciseId);
        var update = ScoreAggregator.Apply(
            aggregate?.CorrectCount ?? 0,
            aggregate?.ErrorCount ?? 0,
            aggregate?.BestScore ?? 0,
            isCorrect);
        var now = DateTime.UtcNow;

        if (aggregate is null)
        {
            db.ScoreAggregates.Add(new ScoreAggregate
            {
                UserId = userId,
                ExerciseId = exerciseId,
                CorrectCount = update.CorrectCount,
                ErrorCount = update.ErrorCount,
                BestScore = update.BestScore,
                LastAttemptAt = now
            });
        }
        else
        {
            aggregate.CorrectCount = update.CorrectCount;
            aggregate.ErrorCount = update.ErrorCount;
            aggregate.BestScore = update.BestScore;
            aggregate.LastAttemptAt = now;
        }

        await db.SaveChangesAsync();
    }

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@example.test";
}
