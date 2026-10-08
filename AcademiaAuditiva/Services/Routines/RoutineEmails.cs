using System.Globalization;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Services.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>What became of the e-mails about a routine just assigned.</summary>
/// <param name="Students">The students it went to who get these e-mails: they confirmed their address and didn't turn them off.</param>
/// <param name="Queued">How many of them will get one; the others are past the day's limit.</param>
/// <param name="Failed">Nothing could be queued.</param>
public sealed record RoutineEmailCount(int Students, int Queued, bool Failed = false);

/// <summary>The e-mails about one assignment, sent in the background.</summary>
/// <param name="StudentIds">In the order they are sent.</param>
/// <param name="Culture">For students who never chose a language: the one the teacher was using.</param>
public sealed record RoutineEmailJob(int AssignmentId, IReadOnlyList<string> StudentIds, string Culture);

/// <summary>
/// E-mails the students a teacher assigns a routine to, within the day's limit
/// (<see cref="NotificationEmailOptions.DailyLimit"/>): in the background, each in the student's
/// language (<see cref="Models.ApplicationUser.Language"/>). Students who haven't confirmed their
/// address, or who turned these e-mails off (<see cref="Models.ApplicationUser.RoutineEmailsOff"/>), get none.
/// </summary>
public sealed class RoutineEmails
{
    private readonly ApplicationDbContext _db;
    private readonly SmtpOptions _smtp;
    private readonly NotificationEmailOptions _options;
    private readonly NotificationEmailQuota _quota;
    private readonly BackgroundEmailQueue _queue;
    private readonly EmailComposer _composer;
    private readonly IEmailMessageSender _sender;
    private readonly RequestLocalizationOptions _localization;
    private readonly ILogger<RoutineEmails> _logger;

    public RoutineEmails(
        ApplicationDbContext db,
        IOptions<SmtpOptions> smtp,
        IOptionsSnapshot<NotificationEmailOptions> options,
        NotificationEmailQuota quota,
        BackgroundEmailQueue queue,
        EmailComposer composer,
        IEmailMessageSender sender,
        IOptions<RequestLocalizationOptions> localization,
        ILogger<RoutineEmails> logger)
    {
        _db = db;
        _smtp = smtp.Value;
        _options = options.Value;
        _quota = quota;
        _queue = queue;
        _composer = composer;
        _sender = sender;
        _localization = localization.Value;
        _logger = logger;
    }

    /// <summary>Whether students are e-mailed their routines: mail is set up and the daily limit isn't 0.</summary>
    public bool Enabled => _smtp.IsConfigured && _options.DailyLimit > 0;

    /// <summary>
    /// Queues the e-mails about an assignment just saved, as many as the day's limit allows.
    /// It never throws: the assignment stands whatever becomes of its e-mails.
    /// </summary>
    public async Task<RoutineEmailCount> QueueAsync(int assignmentId, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return new RoutineEmailCount(0, 0);
        try
        {
            var students = await RecipientsAsync(assignmentId, cancellationToken);
            if (students.Count == 0) return new RoutineEmailCount(0, 0);

            var queued = await _quota.ReserveAsync(students.Count, cancellationToken);
            if (queued < students.Count)
            {
                _logger.LogWarning(
                    "The daily e-mail limit is reached: {Skipped} of {Students} students won't be e-mailed about routine assignment {AssignmentId}",
                    students.Count - queued, students.Count, assignmentId);
            }

            if (queued > 0)
            {
                var job = new RoutineEmailJob(assignmentId, students.Take(queued).Select(s => s.Id).ToList(), CultureInfo.CurrentUICulture.Name);
                _queue.Enqueue((services, token) => services.GetRequiredService<RoutineEmails>().SendAsync(job, token));
            }

            return new RoutineEmailCount(students.Count, queued);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not queue the e-mails about routine assignment {AssignmentId}", assignmentId);
            return new RoutineEmailCount(0, 0, Failed: true);
        }
    }

    /// <summary>
    /// Sends the e-mails of a job (<see cref="BackgroundEmailWorker"/> runs it), but not to the
    /// students who no longer get them; none if the routine was unassigned in the meantime.
    /// A failed e-mail is logged and the others still go.
    /// </summary>
    public async Task SendAsync(RoutineEmailJob job, CancellationToken cancellationToken)
    {
        var assignment = await _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.Id == job.AssignmentId && a.ClassroomId != null)
            .Select(a => new
            {
                Routine = a.Routine!.Name,
                Classroom = a.Classroom!.Name,
                a.DueAt,
                a.AllowLate,
                a.Routine.Owner!.FirstName,
                a.Routine.Owner.LastName,
                a.Routine.Owner.Email,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (assignment is null) return;

        var teacher = EmailComposer.DescribeTeacher(assignment.FirstName, assignment.LastName, assignment.Email);
        var students = (await RecipientsAsync(job.AssignmentId, cancellationToken)).ToDictionary(s => s.Id);

        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            foreach (var id in job.StudentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!students.TryGetValue(id, out var student)) continue;

                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureOf(student.Language, job.Culture);
                try
                {
                    var message = await _composer.RoutineAssignedAsync(
                        teacher, assignment.Classroom, assignment.Routine, assignment.DueAt, assignment.AllowLate);
                    await _sender.SendEmailAsync(student.Email, message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Could not e-mail {Email} about routine assignment {AssignmentId}",
                        LogSanitizer.HashEmail(student.Email), job.AssignmentId);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private sealed record Recipient(string Id, string Email, string? Language);

    // The members of the classroom the routine went to (only those ticked, if the teacher ticked
    // some) who get these e-mails, by user name.
    private async Task<List<Recipient>> RecipientsAsync(int assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Select(a => new { a.ClassroomId, a.ChosenStudentsOnly })
            .FirstOrDefaultAsync(cancellationToken);
        if (assignment?.ClassroomId is not int classroomId) return [];

        var chosenOnly = assignment.ChosenStudentsOnly;
        return await _db.ClassroomMembers.AsNoTracking()
            .Where(m => m.ClassroomId == classroomId)
            .Where(m => !chosenOnly || _db.RoutineAssignmentStudents
                .Any(s => s.RoutineAssignmentId == assignmentId && s.StudentId == m.StudentId))
            .Select(m => m.Student!)
            .Where(u => u.EmailConfirmed && u.Email != null && u.Email != "" && !u.RoutineEmailsOff)
            .OrderBy(u => u.UserName).ThenBy(u => u.Id)
            .Select(u => new Recipient(u.Id, u.Email!, u.Language))
            .ToListAsync(cancellationToken);
    }

    // The student's language if the site has it, else the teacher's, else the site's default.
    private CultureInfo CultureOf(string? language, string fallback)
    {
        var cultures = _localization.SupportedUICultures ?? [];
        return cultures.FirstOrDefault(c => string.Equals(c.Name, language, StringComparison.OrdinalIgnoreCase))
            ?? cultures.FirstOrDefault(c => string.Equals(c.Name, fallback, StringComparison.OrdinalIgnoreCase))
            ?? _localization.DefaultRequestCulture.UICulture;
    }
}
