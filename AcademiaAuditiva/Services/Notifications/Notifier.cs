using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Routines;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.Notifications;

/// <summary>
/// Writes the notifications shown under the bell (<see cref="Notification"/>). They are a bonus:
/// these methods log what fails rather than throw, unless their token is cancelled, so what the
/// user did stands without them. Nobody is told of what they did themself, such as a teacher who
/// joined their own classroom.
/// </summary>
public sealed class Notifier
{
    /// <summary>
    /// From this hour (UTC) on, students are reminded of the routines due the next day. Students'
    /// time zones aren't stored, so days are UTC days: from noon UTC, the next day is the same one
    /// from Canada to France and Brazil.
    /// </summary>
    public const int ReminderHourUtc = 12;

    private readonly ApplicationDbContext _db;
    private readonly RoutineRounds _rounds;
    private readonly TimeProvider _clock;
    private readonly ILogger<Notifier> _logger;

    public Notifier(ApplicationDbContext db, RoutineRounds rounds, TimeProvider clock, ILogger<Notifier> logger)
    {
        _db = db;
        _rounds = rounds;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Tells the students of an assignment just saved that they have a new routine.</summary>
    public async Task RoutineAssignedAsync(int assignmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var students = await RecipientsAsync(assignmentId, cancellationToken);
            await NotifyAsync(assignmentId, NotificationKind.RoutineAssigned, students, teacherId: null, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not notify the students of routine assignment {AssignmentId}", assignmentId);
        }
    }

    /// <summary>
    /// Tells the teacher once the student has answered every question of the routine. Called when
    /// an answer completes one of its items, so other items may still be left.
    /// </summary>
    public async Task RoutineItemCompletedAsync(string studentId, int assignmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var routine = await _rounds.FindRoutineAsync(studentId, assignmentId, TimeZoneInfo.Utc, cancellationToken);
            if (routine is not { IsFinished: true }) return;

            var teacherId = await _db.RoutineAssignments.AsNoTracking()
                .Where(a => a.Id == assignmentId)
                .Select(a => a.Routine!.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (teacherId is null || teacherId == studentId) return;

            await NotifyAsync(assignmentId, NotificationKind.StudentFinishedRoutine, [studentId], teacherId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not tell the teacher that a student finished routine assignment {AssignmentId}", assignmentId);
        }
    }

    /// <summary>Tells the teacher that a student accepted their invitation and joined the classroom.</summary>
    public async Task InviteAcceptedAsync(int classroomId, string teacherId, string studentId, CancellationToken cancellationToken = default)
    {
        if (teacherId == studentId) return;
        try
        {
            await SaveAsync([new Notification
            {
                UserId = teacherId,
                StudentId = studentId,
                Kind = NotificationKind.InviteAccepted,
                ClassroomId = classroomId,
                CreatedAt = Now,
            }], cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not tell the teacher that a student joined classroom {ClassroomId}", classroomId);
        }
    }

    /// <summary>
    /// From <see cref="ReminderHourUtc"/>, reminds the students of the routines due the next day
    /// that they haven't finished, once each. Routines assigned that day are left out: their
    /// students were just told of them, with the due date. <see cref="NotificationsJob"/> runs it
    /// every hour on every replica, so students who join a class later that day are reminded too.
    /// </summary>
    /// <returns>How many students were reminded.</returns>
    public async Task<int> RemindDueTomorrowAsync(CancellationToken cancellationToken = default)
    {
        var now = Now;
        if (now.Hour < ReminderHourUtc) return 0;

        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var dayAfter = today.AddDays(2);
        var assignmentIds = await _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.DueAt >= tomorrow && a.DueAt < dayAfter && a.AssignedAt < today)
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var reminded = 0;
        foreach (var assignmentId in assignmentIds)
        {
            try
            {
                var told = await NotifiedStudentsAsync(assignmentId, NotificationKind.RoutineDueTomorrow, cancellationToken);
                var students = new List<string>();
                foreach (var studentId in (await RecipientsAsync(assignmentId, cancellationToken)).Where(s => !told.Contains(s)))
                {
                    var routine = await _rounds.FindRoutineAsync(studentId, assignmentId, TimeZoneInfo.Utc, cancellationToken);
                    if (routine is { Items.Count: > 0, IsFinished: false }) students.Add(studentId);
                }
                reminded += await NotifyAsync(assignmentId, NotificationKind.RoutineDueTomorrow, students, teacherId: null, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Could not remind the students of routine assignment {AssignmentId} that it is due tomorrow", assignmentId);
            }
        }
        return reminded;
    }

    /// <summary>Deletes the notifications older than <see cref="Notification.Retention"/>, read or not.</summary>
    /// <returns>How many were deleted.</returns>
    public Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = Now - Notification.Retention;
        return _db.Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    // The students an assignment goes to, but its teacher: the classroom's members, or those of
    // them the teacher ticked; for an older assignment, its one student.
    private async Task<List<string>> RecipientsAsync(int assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Select(a => new { a.ClassroomId, a.StudentId, a.ChosenStudentsOnly, TeacherId = a.Routine!.OwnerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (assignment is null) return [];

        List<string> students;
        if (assignment.ClassroomId is int classroomId)
        {
            var chosenOnly = assignment.ChosenStudentsOnly;
            students = await _db.ClassroomMembers.AsNoTracking()
                .Where(m => m.ClassroomId == classroomId)
                .Where(m => !chosenOnly || _db.RoutineAssignmentStudents
                    .Any(s => s.RoutineAssignmentId == assignmentId && s.StudentId == m.StudentId))
                .OrderBy(m => m.StudentId)
                .Select(m => m.StudentId)
                .ToListAsync(cancellationToken);
        }
        else
        {
            students = assignment.StudentId is { } studentId ? [studentId] : [];
        }
        return students.Where(s => s != assignment.TeacherId).ToList();
    }

    private async Task<HashSet<string>> NotifiedStudentsAsync(int assignmentId, NotificationKind kind, CancellationToken cancellationToken)
        => (await _db.Notifications.AsNoTracking()
            .Where(n => n.RoutineAssignmentId == assignmentId && n.Kind == kind)
            .Select(n => n.StudentId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

    // Tells those of the students who weren't told yet; a teacher's notifications about them go to
    // the teacher. Another request or replica may tell some meanwhile: the unique index then
    // refuses the save, which is no error once they have all been told.
    private async Task<int> NotifyAsync(int assignmentId, NotificationKind kind, IReadOnlyCollection<string> studentIds,
        string? teacherId, CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0) return 0;

        var told = await NotifiedStudentsAsync(assignmentId, kind, cancellationToken);
        var now = Now;
        var notifications = studentIds.Where(s => !told.Contains(s)).Distinct().Select(s => new Notification
        {
            UserId = teacherId ?? s,
            StudentId = s,
            Kind = kind,
            RoutineAssignmentId = assignmentId,
            CreatedAt = now,
        }).ToList();

        try
        {
            await SaveAsync(notifications, cancellationToken);
            return notifications.Count;
        }
        catch (DbUpdateException)
        {
            told = await NotifiedStudentsAsync(assignmentId, kind, cancellationToken);
            if (!notifications.All(n => told.Contains(n.StudentId))) throw;
            _logger.LogInformation("Another request saved the {Kind} notifications of routine assignment {AssignmentId} first", kind, assignmentId);
            return 0;
        }
    }

    // Saves the notifications just made. Should that fail they are dropped, so the request's
    // context can still save its own changes.
    private async Task SaveAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken)
    {
        if (notifications.Count == 0) return;

        _db.Notifications.AddRange(notifications);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var notification in notifications)
            {
                _db.Entry(notification).State = EntityState.Detached;
            }
            throw;
        }
    }
}
