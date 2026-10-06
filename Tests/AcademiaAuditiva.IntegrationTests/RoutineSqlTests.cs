using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Routine answers on SQL Server: the unique index on their tag keeps two windows from
/// taking the same question, and the student's routines are read with real SQL.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class RoutineSqlTests
{
    private readonly RealSqlServerFixture _fixture;

    public RoutineSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task RoutineQuestion_TakesASingleAnswer()
    {
        var seeded = await SeedAsync();

        await using (var db = _fixture.CreateContext())
        {
            db.ScoreSnapshots.AddRange(seeded.Answer(item: 0, question: 1), seeded.Practice(), seeded.Practice());
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            db.ScoreSnapshots.Add(seeded.Answer(item: 0, question: 1));
            var again = () => db.SaveChangesAsync();
            await again.Should().ThrowAsync<DbUpdateException>("another window took that question");
        }

        await using (var db = _fixture.CreateContext())
        {
            db.ScoreSnapshots.AddRange(seeded.Answer(item: 0, question: 2), seeded.Answer(item: 1, question: 1));
            await db.SaveChangesAsync();
            (await db.ScoreSnapshots.CountAsync(s => s.RoutineAssignmentId == seeded.AssignmentId)).Should().Be(3);
            (await db.ScoreSnapshots.CountAsync(s => s.UserId == seeded.StudentId && s.RoutineAssignmentId == null))
                .Should().Be(2, "answers outside routines take no question");
        }
    }

    [RealSqlFact]
    public async Task StudentsRoutines_AreReadWithTheirOwnAnswers()
    {
        var seeded = await SeedAsync();
        int classroomRoutine, archivedRoutine;
        await using (var db = _fixture.CreateContext())
        {
            db.ScoreSnapshots.AddRange(
                seeded.Answer(item: 0, question: 1, isCorrect: true),
                seeded.Answer(item: 0, question: 2, isCorrect: false),
                seeded.Answer(item: 1, question: 1, isCorrect: true),
                seeded.Practice(), seeded.Practice());
            db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
            {
                RoutineAssignmentId = seeded.AssignmentId,
                StudentId = seeded.StudentId,
                RoutineItemId = seeded.ItemIds[1],
                OverrideTargetCount = 4
            });
            var classroom = new Classroom
            {
                Name = "SQL routine classroom",
                OwnerId = seeded.TeacherId,
                Members = { new ClassroomMember { StudentId = seeded.StudentId } }
            };
            var archived = new Classroom
            {
                Name = "SQL archived classroom",
                OwnerId = seeded.TeacherId,
                IsArchived = true,
                Members = { new ClassroomMember { StudentId = seeded.StudentId } }
            };
            var ofTheClassroom = new RoutineAssignment
            {
                RoutineId = seeded.RoutineId,
                Classroom = classroom,
                DueAt = new DateTime(2026, 1, 20)
            };
            var ofTheArchived = new RoutineAssignment { RoutineId = seeded.RoutineId, Classroom = archived };
            db.RoutineAssignments.AddRange(ofTheClassroom, ofTheArchived);
            await db.SaveChangesAsync();
            (classroomRoutine, archivedRoutine) = (ofTheClassroom.Id, ofTheArchived.Id);
        }

        await using (var db = _fixture.CreateContext())
        {
            var rounds = Rounds(db);

            var routines = await rounds.ListAsync(seeded.StudentId, TimeZoneInfo.Utc);

            routines.Select(r => (r.AssignmentId, r.ClassroomName)).Should().Equal(
                [(classroomRoutine, "SQL routine classroom"), (seeded.AssignmentId, (string?)null)],
                "the routine due soonest comes first, and archived classrooms take no answers");
            var personal = routines[1];
            personal.Items.Select(i => (i.ItemId, i.Progress.Attempts, i.Progress.Correct, i.Progress.Target)).Should().Equal(
                (seeded.ItemIds[0], 2, 1, 2), (seeded.ItemIds[1], 1, 1, 4));
            personal.Items[0].Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["keySelect"] = "D4" });
            routines[0].Items.Select(i => i.Progress.Attempts).Should().Equal([0, 0], "answers count for their own assignment only");

            var found = await rounds.FindAsync(seeded.StudentId, new RoutineLink(seeded.AssignmentId, seeded.ItemIds[1]), TimeZoneInfo.Utc);
            found!.Item.NextQuestion.Should().Be(2);
            (await rounds.FindAsync(seeded.StudentId, new RoutineLink(archivedRoutine, seeded.ItemIds[0]), TimeZoneInfo.Utc))
                .Should().BeNull();
            (await rounds.FindAsync(seeded.TeacherId, new RoutineLink(seeded.AssignmentId, seeded.ItemIds[0]), TimeZoneInfo.Utc))
                .Should().BeNull();
        }
    }

    private static RoutineRounds Rounds(ApplicationDbContext db)
        => new(db, new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), TimeProvider.System);

    private sealed record Seeded(string TeacherId, string StudentId, int ExerciseId, int RoutineId, int AssignmentId, IReadOnlyList<int> ItemIds)
    {
        public ScoreSnapshot Answer(int item, int question, bool isCorrect = true) => new()
        {
            UserId = StudentId,
            ExerciseId = ExerciseId,
            IsCorrect = isCorrect,
            RoutineAssignmentId = AssignmentId,
            RoutineItemId = ItemIds[item],
            RoutineQuestion = question
        };

        public ScoreSnapshot Practice() => new() { UserId = StudentId, ExerciseId = ExerciseId, IsCorrect = true };
    }

    // A routine of two GuessInterval items, assigned to a student alone.
    private async Task<Seeded> SeedAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        SeedData.SeedExercises(db);

        var teacher = NewUser("routine-teacher");
        var student = NewUser("routine-student");
        db.Users.AddRange(teacher, student);
        var exerciseId = await db.Exercises.Where(e => e.Name == "GuessInterval").Select(e => e.ExerciseId).SingleAsync();
        var routine = new Routine
        {
            Name = "SQL routine answers",
            OwnerId = teacher.Id,
            Items =
            {
                new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 2, FilterJson = """{"keySelect":"D4"}""" },
                new RoutineItem { ExerciseId = exerciseId, Order = 2, TargetCount = 3 }
            }
        };
        var assignment = new RoutineAssignment { Routine = routine, StudentId = student.Id };
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new Seeded(teacher.Id, student.Id, exerciseId, routine.Id, assignment.Id,
            routine.Items.OrderBy(i => i.Order).Select(i => i.Id).ToArray());
    }

    private static ApplicationUser NewUser(string prefix)
    {
        var email = $"{prefix}.{Guid.NewGuid():N}@example.test";
        return new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
    }
}
