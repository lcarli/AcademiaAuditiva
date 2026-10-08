using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Services.Notifications;

/// <summary>Reads a user's notifications (<see cref="Notification"/>) and marks them read.</summary>
public sealed class NotificationInbox
{
    /// <summary>The bell counts the unread notifications up to this; past it, it shows "9+".</summary>
    public const int MostCounted = 9;

    /// <summary>The page lists the newest ones only; older ones are deleted in time anyway.</summary>
    public const int MostListed = 100;

    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public NotificationInbox(ApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>How many of the user's notifications are unread, up to <see cref="MostCounted"/> + 1.</summary>
    public Task<int> UnreadCountAsync(string userId, CancellationToken cancellationToken = default)
        => _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .Take(MostCounted + 1)
            .CountAsync(cancellationToken);

    /// <summary>The user's newest notifications, the newest first.</summary>
    public async Task<IReadOnlyList<NotificationItem>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(MostListed)
            .Select(n => new
            {
                n.Id,
                n.Kind,
                n.CreatedAt,
                n.ReadAt,
                Routine = n.RoutineAssignment != null ? n.RoutineAssignment.Routine!.Name : null,
                Classroom = n.Classroom != null
                    ? n.Classroom.Name
                    : n.RoutineAssignment != null && n.RoutineAssignment.Classroom != null ? n.RoutineAssignment.Classroom.Name : null,
                DueAt = n.RoutineAssignment != null ? n.RoutineAssignment.DueAt : null,
                TeacherName = n.RoutineAssignment != null ? n.RoutineAssignment.Routine!.Owner!.UserName : null,
                TeacherEmail = n.RoutineAssignment != null ? n.RoutineAssignment.Routine!.Owner!.Email : null,
                StudentName = n.Student!.UserName,
                StudentEmail = n.Student!.Email,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new NotificationItem(
            r.Id,
            r.Kind,
            r.CreatedAt,
            IsRead: r.ReadAt != null,
            r.Routine,
            r.Classroom,
            r.DueAt,
            Teacher: r.TeacherName ?? r.TeacherEmail,
            Student: string.IsNullOrEmpty(r.StudentEmail) || r.StudentEmail == r.StudentName
                ? r.StudentName
                : $"{r.StudentName} ({r.StudentEmail})")).ToList();
    }

    /// <summary>The user's notification, now read; null when they have none with that id.</summary>
    public async Task<Notification?> OpenAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification is { ReadAt: null })
        {
            notification.ReadAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return notification;
    }

    /// <summary>Marks all of the user's notifications read.</summary>
    /// <returns>How many were unread.</returns>
    public async Task<int> MarkAllReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ToListAsync(cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var notification in unread)
        {
            notification.ReadAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }
}

/// <summary>A notification as listed, with the names it mentions.</summary>
/// <param name="Classroom">The classroom joined, or the routine's; null for a routine assigned to the student alone.</param>
/// <param name="DueAt">The routine's due date (no time), if any.</param>
/// <param name="Teacher">Who assigned the routine, for its notifications.</param>
/// <param name="Student">Who the notification is about, as their teacher sees them.</param>
public sealed record NotificationItem(
    int Id,
    NotificationKind Kind,
    DateTime CreatedAt,
    bool IsRead,
    string? Routine,
    string? Classroom,
    DateTime? DueAt,
    string? Teacher,
    string? Student);
