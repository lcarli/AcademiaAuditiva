using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services;

/// <summary>
/// Backs the "Download" and "Delete" buttons of Manage › Personal data.
/// </summary>
public class PersonalDataService
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep accented names readable; HTML-sensitive characters stay escaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<PersonalDataService> _logger;

    public PersonalDataService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAnalyticsService analytics,
        ILogger<PersonalDataService> logger)
    {
        _db = db;
        _userManager = userManager;
        _analytics = analytics;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the user together with the classes and routines they own.
    /// Practice data and the Identity tables cascade in the database, but the
    /// teaching tables restrict user deletes (or would be left orphaned by
    /// SET NULL), so they are removed here. The user store shares this scoped
    /// DbContext, so everything is written by the single SaveChanges inside
    /// <see cref="UserManager{TUser}.DeleteAsync"/>, in one transaction.
    /// </summary>
    public async Task<IdentityResult> DeleteAccountAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var userId = user.Id;
        var email = NormalizeInviteEmail(user.Email);

        var routines = await _db.Routines.Where(r => r.OwnerId == userId).ToListAsync(ct);
        var routineIds = routines.Select(r => r.Id).ToList();

        var classrooms = await _db.Classrooms.Where(c => c.OwnerId == userId).ToListAsync(ct);
        var classroomIds = classrooms.Select(c => c.Id).ToList();

        var items = await _db.RoutineItems.Where(i => routineIds.Contains(i.RoutineId)).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();

        var assignments = await _db.RoutineAssignments
            .Where(a => a.StudentId == userId
                || routineIds.Contains(a.RoutineId)
                || (a.ClassroomId != null && classroomIds.Contains(a.ClassroomId.Value)))
            .ToListAsync(ct);
        var assignmentIds = assignments.Select(a => a.Id).ToList();

        // Loaded explicitly (not left to the assignment cascade) so EF deletes
        // them before the routine items they reference.
        var overrides = await _db.RoutineAssignmentOverrides
            .Where(o => o.StudentId == userId
                || assignmentIds.Contains(o.RoutineAssignmentId)
                || itemIds.Contains(o.RoutineItemId))
            .ToListAsync(ct);

        var members = await _db.ClassroomMembers
            .Where(m => m.StudentId == userId || classroomIds.Contains(m.ClassroomId))
            .ToListAsync(ct);

        var invites = await _db.ClassroomInvites
            .Where(i => classroomIds.Contains(i.ClassroomId)
                || i.CreatedById == userId
                || (email != null && i.Email == email))
            .ToListAsync(ct);

        _db.RoutineAssignmentOverrides.RemoveRange(overrides);
        _db.RoutineAssignments.RemoveRange(assignments);
        _db.RoutineItems.RemoveRange(items);
        _db.Routines.RemoveRange(routines);
        _db.ClassroomInvites.RemoveRange(invites);
        _db.ClassroomMembers.RemoveRange(members);
        _db.Classrooms.RemoveRange(classrooms);

        IdentityResult result;
        try
        {
            result = await _userManager.DeleteAsync(user);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }

        if (!result.Succeeded)
        {
            _db.ChangeTracker.Clear();
            return result;
        }

        _logger.LogInformation(
            "Deleted user {UserId} with {ClassroomCount} classrooms, {RoutineCount} routines, " +
            "{MembershipCount} memberships and {AssignmentCount} assignments.",
            userId, classrooms.Count, routines.Count, members.Count, assignments.Count);

        await _analytics.DeleteAttemptsAsync(userId);
        return result;
    }

    /// <summary>True when the user owns classes or routines, which <see cref="DeleteAccountAsync"/> removes too.</summary>
    public async Task<bool> OwnsTeachingDataAsync(string userId, CancellationToken ct = default)
        => await _db.Classrooms.AnyAsync(c => c.OwnerId == userId, ct)
            || await _db.Routines.AnyAsync(r => r.OwnerId == userId, ct);

    /// <summary>How many classes and routines the user owns, which <see cref="DeleteAccountAsync"/> removes too.</summary>
    public async Task<(int Classrooms, int Routines)> CountTeachingDataAsync(string userId, CancellationToken ct = default)
        => (await _db.Classrooms.CountAsync(c => c.OwnerId == userId, ct),
            await _db.Routines.CountAsync(r => r.OwnerId == userId, ct));

    /// <summary>
    /// Serialises everything stored about the user as indented JSON. Other
    /// people's data (a teacher's students and invitees) is only counted.
    /// </summary>
    public async Task<byte[]> ExportAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var userId = user.Id;
        var email = NormalizeInviteEmail(user.Email);

        var profile = new Dictionary<string, string?>();
        foreach (var property in typeof(ApplicationUser).GetProperties()
                     .Where(p => Attribute.IsDefined(p, typeof(PersonalDataAttribute))))
        {
            profile[property.Name] = property.GetValue(user)?.ToString();
        }
        foreach (var login in await _userManager.GetLoginsAsync(user))
        {
            profile[$"{login.LoginProvider} external login provider key"] = login.ProviderKey;
        }
        profile["Authenticator Key"] = await _userManager.GetAuthenticatorKeyAsync(user);

        var totals = await _db.ScoreAggregates.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.ExerciseId)
            .Select(a => new { Exercise = a.Exercise!.Name, a.CorrectCount, a.ErrorCount, a.BestScore, a.LastAttemptAt })
            .ToListAsync(ct);

        var answers = await _db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp)
            .Select(s => new { Exercise = s.Exercise!.Name, s.IsCorrect, s.TimeSpentSeconds, s.Timestamp, s.FilterJson, s.GameRunId })
            .ToListAsync(ct);

        var games = await _db.GameRuns.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.StartedAt)
            .Select(r => new
            {
                r.Id,
                r.Mode,
                Exercise = r.Exercise != null ? r.Exercise.Name : null,
                r.FilterJson,
                r.StartedAt,
                r.EndedAt,
                r.Score,
                r.Answered,
                r.PlacementUnit,
                r.AppliedAt
            })
            .ToListAsync(ct);

        var sessions = await _db.Scores.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Timestamp)
            .Select(s => new { Exercise = s.Exercise.Name, s.CorrectCount, s.ErrorCount, s.BestScore, s.TimeSpentSeconds, s.Timestamp })
            .ToListAsync(ct);

        var badges = await _db.BadgesEarned.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.EarnedDate)
            .Select(b => new { Badge = b.BadgeKey, b.EarnedDate })
            .ToListAsync(ct);

        var tutorials = await _db.UserTutorials.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.SeenAt)
            .Select(t => new { Tutorial = t.TutorialKey, t.SeenAt, t.Finished })
            .ToListAsync(ct);

        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Plan, s.Status, s.StartDate, s.EndDate, s.Gateway })
            .ToListAsync(ct);

        var memberships = await _db.ClassroomMembers.AsNoTracking()
            .Where(m => m.StudentId == userId)
            .Select(m => new { m.ClassroomId, Classroom = m.Classroom!.Name, m.JoinedAt })
            .ToListAsync(ct);
        var memberClassroomIds = memberships.Select(m => m.ClassroomId).ToList();

        var assignedRoutines = await _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.StudentId == userId
                || (a.ClassroomId != null && memberClassroomIds.Contains(a.ClassroomId.Value)))
            .OrderBy(a => a.AssignedAt)
            .Select(a => new
            {
                Routine = a.Routine!.Name,
                Classroom = a.Classroom != null ? a.Classroom.Name : null,
                a.AssignedAt,
                a.DueAt
            })
            .ToListAsync(ct);

        var invitations = await _db.ClassroomInvites.AsNoTracking()
            .Where(i => email != null && i.Email == email)
            .Select(i => new { Classroom = i.Classroom!.Name, i.CreatedAt, i.ExpiresAt, i.AcceptedAt })
            .ToListAsync(ct);

        var ownedClassrooms = await _db.Classrooms.AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Name,
                c.Description,
                c.CreatedAt,
                c.IsArchived,
                Students = c.Members.Count,
                Invitations = c.Invites.Count
            })
            .ToListAsync(ct);

        var ownedRoutines = await _db.Routines.AsNoTracking()
            .Where(r => r.OwnerId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new
            {
                r.Name,
                r.Description,
                r.CreatedAt,
                Items = r.Items
                    .OrderBy(i => i.Order)
                    .Select(i => new { Exercise = i.Exercise!.Name, i.Order, i.TargetCount, i.MinScore, i.FilterJson })
                    .ToList()
            })
            .ToListAsync(ct);

        var export = new Dictionary<string, object?>
        {
            ["profile"] = profile,
            ["roles"] = await _userManager.GetRolesAsync(user),
            ["practice"] = new
            {
                Totals = totals,
                Answers = answers,
                Sessions = sessions,
                AttemptLogs = await _analytics.GetAttemptsAsync(userId)
            },
            ["games"] = games,
            ["badges"] = badges,
            ["tutorials"] = tutorials,
            ["subscriptions"] = subscriptions,
            ["student"] = new
            {
                Classrooms = memberships.Select(m => new { m.Classroom, m.JoinedAt }),
                AssignedRoutines = assignedRoutines,
                Invitations = invitations
            },
            ["teacher"] = new { Classrooms = ownedClassrooms, Routines = ownedRoutines }
        };

        return JsonSerializer.SerializeToUtf8Bytes(export, ExportJsonOptions);
    }

    // Invites are stored trimmed and lower-cased (see MembersController).
    private static string? NormalizeInviteEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
