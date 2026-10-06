using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The teacher's reports (#107) read with real SQL: they count only the answers tagged with the
/// teacher's own assignments, from students in one of the teacher's classrooms.
/// </summary>
[Collection(RealSqlServerCollection.Name)]
public sealed class RoutineReportSqlTests
{
    private static readonly DateTime Monday = new(2026, 1, 19, 12, 0, 0, DateTimeKind.Utc);

    private readonly RealSqlServerFixture _fixture;

    public RoutineReportSqlTests(RealSqlServerFixture fixture) => _fixture = fixture;

    [RealSqlFact]
    public async Task Reports_CountTheAnswersToTheTeachersOwnAssignments()
    {
        int choirId, bandId, ofTheChoirId, ofBrunoId, ofTheBandId;
        ApplicationUser teacher, otherTeacher, ana, bruno, carla;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            SeedData.SeedExercises(db);

            (teacher, otherTeacher) = (NewUser("report-teacher"), NewUser("report-other"));
            (ana, bruno, carla) = (NewUser("report-ana"), NewUser("report-bruno"), NewUser("report-carla"));
            db.Users.AddRange(teacher, otherTeacher, ana, bruno, carla);
            var exerciseId = await db.Exercises.Where(e => e.Name == "GuessInterval").Select(e => e.ExerciseId).SingleAsync();

            var choir = new Classroom
            {
                Name = "SQL report choir",
                OwnerId = teacher.Id,
                Members = { new ClassroomMember { StudentId = ana.Id }, new ClassroomMember { StudentId = bruno.Id } }
            };
            var archived = new Classroom
            {
                Name = "SQL report archive",
                OwnerId = teacher.Id,
                IsArchived = true,
                Members = { new ClassroomMember { StudentId = bruno.Id } }
            };
            var band = new Classroom
            {
                Name = "SQL report band",
                OwnerId = otherTeacher.Id,
                Members = { new ClassroomMember { StudentId = ana.Id }, new ClassroomMember { StudentId = carla.Id } }
            };
            var routine = new Routine
            {
                Name = "SQL report routine",
                OwnerId = teacher.Id,
                Items =
                {
                    new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 2 },
                    new RoutineItem { ExerciseId = exerciseId, Order = 2, TargetCount = 1 }
                }
            };
            var fanfare = new Routine
            {
                Name = "SQL report fanfare",
                OwnerId = otherTeacher.Id,
                Items = { new RoutineItem { ExerciseId = exerciseId, Order = 1, TargetCount = 2 } }
            };
            // Due on Sunday, taking late answers; the reports are read on Monday.
            var ofTheChoir = new RoutineAssignment
            {
                Routine = routine, Classroom = choir, AssignedAt = Monday.AddDays(-7), DueAt = new DateTime(2026, 1, 18), AllowLate = true
            };
            var ofBruno = new RoutineAssignment { Routine = routine, StudentId = bruno.Id, AssignedAt = Monday.AddDays(-1) };
            var ofTheBand = new RoutineAssignment { Routine = fanfare, Classroom = band, AssignedAt = Monday.AddDays(-2) };
            db.Classrooms.Add(archived);
            db.RoutineAssignments.AddRange(ofTheChoir, ofBruno, ofTheBand);
            await db.SaveChangesAsync();

            var items = routine.Items.OrderBy(i => i.Order).Select(i => i.Id).ToArray();
            ScoreSnapshot Answer(ApplicationUser student, RoutineAssignment assignment, int itemId, int question, bool isCorrect, int seconds, DateTime at)
                => new()
                {
                    UserId = student.Id,
                    ExerciseId = exerciseId,
                    IsCorrect = isCorrect,
                    TimeSpentSeconds = seconds,
                    Timestamp = at,
                    RoutineAssignmentId = assignment.Id,
                    RoutineItemId = itemId,
                    RoutineQuestion = question
                };
            db.ScoreSnapshots.AddRange(
                Answer(ana, ofTheChoir, items[0], 1, true, 4, Monday.AddDays(-3)),
                Answer(ana, ofTheChoir, items[0], 2, false, 6, Monday.AddDays(-3)),
                Answer(ana, ofTheChoir, items[1], 1, true, 5, Monday.AddDays(-2)),
                Answer(bruno, ofBruno, items[0], 1, true, 3, Monday.AddHours(-2)),
                Answer(ana, ofTheBand, fanfare.Items.Single().Id, 1, true, 9, Monday.AddHours(-1)),
                new ScoreSnapshot { UserId = ana.Id, ExerciseId = exerciseId, IsCorrect = true, TimeSpentSeconds = 2, Timestamp = Monday });
            db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
            {
                RoutineAssignmentId = ofTheChoir.Id, StudentId = bruno.Id, RoutineItemId = items[1], ExcludeItem = true
            });
            await db.SaveChangesAsync();
            (choirId, bandId, ofTheChoirId, ofBrunoId, ofTheBandId) = (choir.Id, band.Id, ofTheChoir.Id, ofBruno.Id, ofTheBand.Id);
        }

        await using var read = _fixture.CreateContext();
        var reports = new RoutineReports(read, new AnswerTimeTests.ManualClock(new DateTimeOffset(Monday)));
        var utc = TimeZoneInfo.Utc;

        var choirRoutine = (await reports.AssignmentAsync(teacher.Id, ofTheChoirId, utc))!;
        choirRoutine.Takes.Select(t => (t.Student.Id, t.Status, t.IsLate, t.Attempts, t.Correct)).Should().Equal(
            (ana.Id, TakeStatus.Finished, false, 3, 2),
            (bruno.Id, TakeStatus.NotStarted, true, 0, 0));
        choirRoutine.Takes[0].Items.Select(i => i!.Seconds).Should().Equal(10L, 5L);
        choirRoutine.Takes[1].Items[1].Should().BeNull("the item is excluded for him");
        choirRoutine.Items.Select(i => (i.Students, i.Completed, i.Attempts, i.Correct, i.Seconds)).Should().Equal(
            (2, 1, 2, 1, 10L), (1, 1, 1, 1, 5L));

        var choirClass = (await reports.ClassroomAsync(teacher.Id, choirId, utc))!;
        choirClass.Routines.Select(r => (r.Assignment.Id, r.Totals.Takes, r.Totals.Finished, r.Totals.Attempts)).Should().Equal(
            (ofBrunoId, 1, 0, 1), (ofTheChoirId, 2, 1, 3));
        choirClass.Students.Select(s => (s.Student.Id, s.Totals.Takes, s.Totals.Attempts, s.LastAnswerAt)).Should().Equal(
            (ana.Id, 1, 3, (DateTime?)Monday.AddDays(-2)),
            (bruno.Id, 2, 1, (DateTime?)Monday.AddHours(-2)));

        var brunoReport = (await reports.StudentAsync(teacher.Id, bruno.Id, utc))!;
        brunoReport.Classrooms.Select(c => c.Name).Should().Equal("SQL report archive", "SQL report choir");
        brunoReport.Routines.Select(r => (r.Assignment.Id, r.Take.Status, r.Take.Attempts)).Should().Equal(
            (ofBrunoId, TakeStatus.InProgress, 1), (ofTheChoirId, TakeStatus.NotStarted, 0));
        var anaReport = (await reports.StudentAsync(teacher.Id, ana.Id, utc))!;
        anaReport.Routines.Select(r => r.Assignment.Id).Should().Equal(ofTheChoirId);
        anaReport.Totals.Attempts.Should().Be(3, "her practice and another teacher's routine don't count");

        (await reports.AssignmentAsync(teacher.Id, ofTheBandId, utc)).Should().BeNull();
        (await reports.ClassroomAsync(teacher.Id, bandId, utc)).Should().BeNull();
        (await reports.StudentAsync(teacher.Id, carla.Id, utc)).Should().BeNull();
        (await reports.AssignmentAsync(otherTeacher.Id, ofTheBandId, utc))!.Takes.Select(t => (t.Student.Id, t.Attempts))
            .Should().Equal((ana.Id, 1), (carla.Id, 0));
    }

    private static ApplicationUser NewUser(string prefix)
    {
        var email = $"{prefix}.{Guid.NewGuid():N}@example.test";
        return new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
    }
}
