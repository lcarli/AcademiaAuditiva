using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// <see cref="ExploreWebApplicationFactory"/> (no storage) for routines taken like a test: its
/// clock stands still at <see cref="Now"/>, the attempt log only records, and it seeds routines
/// that <see cref="TeacherId"/> assigns to <see cref="SignedInWebApplicationFactory.UserId"/>
/// from the real exercise catalogue.
/// </summary>
public sealed class RoutineWebApplicationFactory : ExploreWebApplicationFactory
{
    public const string TeacherId = "routine-teacher";

    public const string Toronto = "America/Toronto";

    /// <summary>22:00 on 14 January in Toronto (UTC-5), and already the 15th in UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 3, 0, 0, TimeSpan.Zero);

    public AnswerTimeTests.ManualClock Clock { get; } = new(Now);

    public FreePracticeTests.RecordingAnalytics Analytics { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IAnalyticsService>();
            services.AddSingleton<IAnalyticsService>(Analytics);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!db.Users.Any(u => u.Id == TeacherId))
            {
                const string email = "routine-teacher@example.test";
                db.Users.Add(new ApplicationUser
                {
                    Id = TeacherId,
                    UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(),
                    Email = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    EmailConfirmed = true,
                    FirstName = "Routine",
                    LastName = "Teacher"
                });
                db.SaveChanges();
            }
        });
    }

    public IAudioTokenService Tokens => Services.GetRequiredService<IAudioTokenService>();

    public int ExerciseId(string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
        return db.Exercises.Single(e => e.Name == name).ExerciseId;
    }

    public RoutineItem Item(string exercise, int target = 2, string? filterJson = null, int? minScore = null)
        => new() { ExerciseId = ExerciseId(exercise), TargetCount = target, FilterJson = filterJson, MinScore = minScore };

    /// <summary>A routine of <paramref name="items"/>, assigned to the signed-in student alone.</summary>
    public Task<SeededRoutine> AssignAsync(params RoutineItem[] items)
        => AssignAsync(new RoutineAssignment { StudentId = SignedInWebApplicationFactory.UserId }, items);

    public async Task<SeededRoutine> AssignAsync(RoutineAssignment assignment, params RoutineItem[] items)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var routine = new Routine { Name = $"Routine {Guid.NewGuid():N}", OwnerId = TeacherId };
        for (var i = 0; i < items.Length; i++)
        {
            items[i].Order = i + 1;
            routine.Items.Add(items[i]);
        }
        assignment.Routine = routine;
        assignment.AssignedAt = Now.UtcDateTime.AddDays(-7);
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new SeededRoutine(routine.Name, assignment.Id, items.Select(i => i.Id).ToArray());
    }

    /// <summary>A classroom of the teacher's with the signed-in student in it.</summary>
    public Classroom ClassroomOfTheStudent(bool archived = false) => new()
    {
        Name = $"Classroom {Guid.NewGuid():N}",
        OwnerId = TeacherId,
        IsArchived = archived,
        Members = { new ClassroomMember { StudentId = SignedInWebApplicationFactory.UserId } }
    };

    public async Task ExcludeAsync(SeededRoutine routine, int item)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
        {
            RoutineAssignmentId = routine.AssignmentId,
            StudentId = SignedInWebApplicationFactory.UserId,
            RoutineItemId = routine.ItemIds[item],
            ExcludeItem = true
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Saves the student's answers to the next questions of a routine item, right or wrong.</summary>
    public async Task RecordAnswersAsync(SeededRoutine routine, int item, params bool[] answers)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var itemId = routine.ItemIds[item];
        var exerciseId = await db.RoutineItems.Where(i => i.Id == itemId).Select(i => i.ExerciseId).SingleAsync();
        var question = await db.ScoreSnapshots.CountAsync(s =>
            s.RoutineAssignmentId == routine.AssignmentId && s.RoutineItemId == itemId);
        foreach (var isCorrect in answers)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId,
                ExerciseId = exerciseId,
                IsCorrect = isCorrect,
                Timestamp = Now.UtcDateTime,
                RoutineAssignmentId = routine.AssignmentId,
                RoutineItemId = itemId,
                RoutineQuestion = ++question
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>The answers saved for a routine, in question order.</summary>
    public async Task<List<ScoreSnapshot>> AnswersAsync(int assignmentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.RoutineAssignmentId == assignmentId)
            .OrderBy(s => s.RoutineItemId).ThenBy(s => s.RoutineQuestion)
            .ToListAsync();
    }

    /// <summary>Saves the student's answers to an exercise outside any routine, since it was assigned.</summary>
    public async Task PracticeAsync(string exercise, int answers)
    {
        var exerciseId = ExerciseId(exercise);
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var i = 0; i < answers; i++)
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = SignedInWebApplicationFactory.UserId,
                ExerciseId = exerciseId,
                IsCorrect = true,
                Timestamp = Now.UtcDateTime
            });
        }
        await db.SaveChangesAsync();
    }
}

/// <param name="ItemIds">The routine's items, in order.</param>
public sealed record SeededRoutine(string Name, int AssignmentId, IReadOnlyList<int> ItemIds)
{
    public int ItemId => ItemIds[0];

    public RoutineLink Link(int item = 0) => new(AssignmentId, ItemIds[item]);

    /// <summary>The exercise page's query for an item, as My Training links to it.</summary>
    public string Query(int item = 0)
        => $"{RoutineLink.AssignmentKey}={AssignmentId}&{RoutineLink.ItemKey}={ItemIds[item]}";
}
