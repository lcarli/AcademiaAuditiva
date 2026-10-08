using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Routines;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Teacher.Services;

/// <summary>
/// The teacher's reports on the routines they assigned. They count only the answers given in
/// those routines (tagged with the assignment, <see cref="AcademiaAuditiva.Models.ScoreSnapshot.RoutineAssignmentId"/>),
/// never practice outside them nor another teacher's routines, and only from students still in
/// one of the teacher's classrooms: a classroom's routine counts its current members (or those of
/// them it was assigned to, when the teacher ticked some), and a routine assigned to one student
/// counts them while they are in one of the teacher's classrooms.
/// Each report is null when what it is about is not the teacher's.
/// </summary>
public sealed class RoutineReports
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public RoutineReports(ApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>One assignment of one of the teacher's routines.</summary>
    public async Task<AssignmentReport?> AssignmentAsync(
        string teacherId, int assignmentId, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var assignment = await Owned(teacherId).FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null) return null;
        await LoadChosenStudentsAsync([assignment], cancellationToken);

        var members = assignment.ClassroomId is { } classroomId
            ? _db.ClassroomMembers.Where(m => m.ClassroomId == classroomId)
            : _db.ClassroomMembers.Where(m => m.StudentId == assignment.StudentId && m.Classroom!.OwnerId == teacherId);
        // A student can be in more than one of the teacher's classrooms.
        var students = Recipients(assignment, await Students(members).ToListAsync(cancellationToken)).DistinctBy(s => s.Id).ToList();

        var takes = (await TakesAsync([assignment], _ => students, timeZone, cancellationToken))[assignment.Id];
        var items = ItemsOf(assignment)
            .Select((item, index) => new ItemSummary(
                item,
                Students: takes.Count(t => t.Items[index] is not null),
                Completed: takes.Count(t => t.Items[index]?.Progress.IsComplete == true),
                Attempts: takes.Sum(t => t.Items[index]?.Progress.Attempts ?? 0),
                Correct: takes.Sum(t => t.Items[index]?.Progress.Correct ?? 0),
                Seconds: takes.Sum(t => t.Items[index]?.Seconds ?? 0)))
            .ToList();
        return new AssignmentReport(Summary(assignment, timeZone), items, takes);
    }

    /// <summary>One of the teacher's classrooms, archived or not.</summary>
    public async Task<ClassroomReport?> ClassroomAsync(
        string teacherId, int classroomId, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var classroom = await _db.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId && c.OwnerId == teacherId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (classroom is null) return null;

        var members = await Students(_db.ClassroomMembers.Where(m => m.ClassroomId == classroomId)).ToListAsync(cancellationToken);
        var memberIds = members.Select(m => m.Id).ToList();
        var assignments = await Owned(teacherId)
            .Where(a => a.ClassroomId == classroomId || (a.StudentId != null && memberIds.Contains(a.StudentId)))
            .ToListAsync(cancellationToken);
        await LoadChosenStudentsAsync(assignments, cancellationToken);

        var takes = await TakesAsync(
            assignments,
            a => a.ClassroomId == classroomId ? Recipients(a, members) : members.Where(m => m.Id == a.StudentId),
            timeZone,
            cancellationToken);

        var routines = Latest(assignments)
            .Select(a => new ClassroomRoutineRow(Summary(a, timeZone), TakeTotals.Of(takes[a.Id])))
            .ToList();
        var students = ByName(members, s => s.UserName)
            .Select(s =>
            {
                var mine = takes.Values.SelectMany(t => t).Where(t => t.Student.Id == s.Id).ToList();
                return new ClassroomStudentRow(s, TakeTotals.Of(mine), mine.Max(t => t.LastAnswerAt));
            })
            .ToList();
        return new ClassroomReport(classroom.Id, classroom.Name, routines, students);
    }

    /// <summary>
    /// A student in one of the teacher's classrooms: the routines the teacher assigned to those
    /// classrooms (archived ones included) or to the student, apart from those that went to other
    /// students the teacher ticked.
    /// </summary>
    public async Task<StudentReport?> StudentAsync(
        string teacherId, string studentId, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var memberships = await _db.ClassroomMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId && m.Classroom!.OwnerId == teacherId)
            .Select(m => new { m.ClassroomId, ClassroomName = m.Classroom!.Name, m.Student!.UserName, m.Student.Email })
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0) return null;

        var student = new ReportStudent(studentId, memberships[0].UserName ?? string.Empty, memberships[0].Email ?? string.Empty);
        var classroomIds = memberships.Select(m => m.ClassroomId).ToList();
        var assignments = await Owned(teacherId)
            .Where(a => (a.ClassroomId != null && classroomIds.Contains(a.ClassroomId.Value)
                    && (!a.ChosenStudentsOnly || a.ChosenStudents.Any(s => s.StudentId == studentId)))
                || a.StudentId == studentId)
            .ToListAsync(cancellationToken);
        await LoadChosenStudentsAsync(assignments, cancellationToken);

        var takes = await TakesAsync(assignments, _ => [student], timeZone, cancellationToken);
        var routines = Latest(assignments)
            .Where(a => takes[a.Id].Count > 0)
            .Select(a => new StudentRoutine(Summary(a, timeZone), ItemsOf(a), takes[a.Id][0]))
            .ToList();
        var classrooms = ByName(memberships, m => m.ClassroomName)
            .Select(m => new ClassroomOption(m.ClassroomId, m.ClassroomName))
            .ToList();
        return new StudentReport(student, classrooms, routines);
    }

    // The teacher's routines, assigned to one of their classrooms or to a student.
    private IQueryable<RoutineAssignment> Owned(string teacherId)
        => _db.RoutineAssignments.AsNoTracking()
            .Where(a => a.Routine!.OwnerId == teacherId && (a.ClassroomId == null || a.Classroom!.OwnerId == teacherId))
            .Include(a => a.Routine!).ThenInclude(r => r.Items).ThenInclude(i => i.Exercise)
            .Include(a => a.Classroom)
            .Include(a => a.Student);

    private static IQueryable<ReportStudent> Students(IQueryable<ClassroomMember> members)
        => members.AsNoTracking().Select(m => new ReportStudent(
            m.StudentId,
            m.Student!.UserName ?? string.Empty,
            m.Student.Email ?? string.Empty));

    // Apart from Owned(): a second collection there would repeat each item once per ticked student.
    private async Task LoadChosenStudentsAsync(IReadOnlyCollection<RoutineAssignment> assignments, CancellationToken cancellationToken)
    {
        var ids = assignments.Where(a => a.ChosenStudentsOnly).Select(a => a.Id).ToList();
        if (ids.Count == 0) return;
        // Those still in the class, like the roster: a student removed from it no longer gets the routine.
        var chosen = await _db.RoutineAssignmentStudents.AsNoTracking()
            .Where(s => ids.Contains(s.RoutineAssignmentId)
                && _db.ClassroomMembers.Any(m => m.ClassroomId == s.RoutineAssignment!.ClassroomId && m.StudentId == s.StudentId))
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            assignment.ChosenStudents = chosen.Where(s => s.RoutineAssignmentId == assignment.Id).ToList();
        }
    }

    // A classroom's routine goes to the whole class, or to the students the teacher ticked in it.
    private static IEnumerable<ReportStudent> Recipients(RoutineAssignment assignment, IEnumerable<ReportStudent> members)
        => assignment.ChosenStudentsOnly
            ? members.Where(m => assignment.ChosenStudents.Any(s => s.StudentId == m.Id))
            : members;

    /// <summary>
    /// Each assignment's takes, by user name: one per student of its roster who has at least one
    /// of its items, with the student's overrides applied and their answers in it counted.
    /// </summary>
    private async Task<Dictionary<int, List<RoutineTake>>> TakesAsync(
        IReadOnlyList<RoutineAssignment> assignments,
        Func<RoutineAssignment, IEnumerable<ReportStudent>> roster,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var rosters = assignments.ToDictionary(a => a.Id, a => roster(a).ToList());
        var takes = rosters.Keys.ToDictionary(id => id, _ => new List<RoutineTake>());
        var assignmentIds = rosters.Keys.ToList();
        var studentIds = rosters.Values.SelectMany(r => r).Select(s => s.Id).Distinct().ToList();
        if (studentIds.Count == 0) return takes;

        var overrides = await _db.RoutineAssignmentOverrides.AsNoTracking()
            .Where(o => assignmentIds.Contains(o.RoutineAssignmentId) && studentIds.Contains(o.StudentId))
            .ToListAsync(cancellationToken);
        var answers = (await _db.ScoreSnapshots.AsNoTracking()
            .Where(s => s.RoutineAssignmentId != null
                && assignmentIds.Contains(s.RoutineAssignmentId.Value)
                && studentIds.Contains(s.UserId))
            .GroupBy(s => new { s.RoutineAssignmentId, s.UserId, s.RoutineItemId })
            .Select(g => new
            {
                g.Key.RoutineAssignmentId,
                g.Key.UserId,
                g.Key.RoutineItemId,
                Attempts = g.Count(),
                Correct = g.Sum(s => s.IsCorrect ? 1 : 0),
                Seconds = g.Sum(s => (long)s.TimeSpentSeconds),
                LastAt = g.Max(s => s.Timestamp)
            })
            .ToListAsync(cancellationToken))
            .ToDictionary(a => (a.RoutineAssignmentId, a.UserId, a.RoutineItemId));

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        foreach (var assignment in assignments)
        {
            var items = assignment.Routine!.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).ToList();
            foreach (var student in ByName(rosters[assignment.Id], s => s.UserName))
            {
                var takeItems = new List<TakeItem?>(items.Count);
                DateTime? lastAnswerUtc = null;
                foreach (var item in items)
                {
                    var @override = overrides.FirstOrDefault(o =>
                        o.RoutineAssignmentId == assignment.Id && o.StudentId == student.Id && o.RoutineItemId == item.Id);
                    if (RoutineItemResolver.Resolve(item, @override) is not { } effective)
                    {
                        takeItems.Add(null);
                        continue;
                    }

                    answers.TryGetValue((assignment.Id, student.Id, item.Id), out var answered);
                    takeItems.Add(new TakeItem(
                        new RoutineItemProgress(answered?.Attempts ?? 0, answered?.Correct ?? 0, effective.Target, effective.MinScore),
                        answered?.Seconds ?? 0));
                    if (answered is not null && (lastAnswerUtc is null || answered.LastAt > lastAnswerUtc))
                    {
                        lastAnswerUtc = answered.LastAt;
                    }
                }

                // Every item is excluded for the student: they have nothing to take.
                if (takeItems.All(i => i is null)) continue;
                takes[assignment.Id].Add(RoutineReportRules.Take(
                    student, takeItems, lastAnswerUtc, assignment.DueAt, assignment.AllowLate, nowUtc, timeZone));
            }
        }
        return takes;
    }

    private static IReadOnlyList<ReportItem> ItemsOf(RoutineAssignment assignment)
        => assignment.Routine!.Items.OrderBy(i => i.Order).ThenBy(i => i.Id)
            .Select((item, index) => new ReportItem(item.Id, index + 1, item.Exercise?.Name ?? string.Empty, item.TargetCount, item.MinScore))
            .ToList();

    private static AssignmentSummary Summary(RoutineAssignment assignment, TimeZoneInfo timeZone) => new(
        Id: assignment.Id,
        RoutineId: assignment.RoutineId,
        RoutineName: assignment.Routine?.Name ?? string.Empty,
        ClassroomId: assignment.ClassroomId,
        ClassroomName: assignment.ClassroomId != null ? assignment.Classroom?.Name ?? string.Empty : null,
        StudentName: assignment.ClassroomId == null ? assignment.Student?.UserName ?? string.Empty : null,
        AssignedOn: PracticeStreak.LocalDate(assignment.AssignedAt, timeZone),
        DueOn: assignment.DueAt is { } due ? DateOnly.FromDateTime(due) : null,
        AllowLate: assignment.AllowLate,
        ChosenStudents: assignment.ChosenStudentsOnly ? assignment.ChosenStudents.Count : null);

    private static IEnumerable<RoutineAssignment> Latest(IEnumerable<RoutineAssignment> assignments)
        => assignments.OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.Id);

    private static IEnumerable<T> ByName<T>(IEnumerable<T> source, Func<T, string> name)
        => source.OrderBy(name, StringComparer.CurrentCultureIgnoreCase);
}

/// <summary>The figures of the reports, apart from the data they are counted from.</summary>
public static class RoutineReportRules
{
    /// <summary>Rounded percentage of right answers, or null before the first answer.</summary>
    public static int? Percent(int correct, int attempts)
        => attempts <= 0 ? null : (int)Math.Round(100.0 * correct / attempts, MidpointRounding.AwayFromZero);

    /// <summary>Rounded seconds per answer, or null before the first answer.</summary>
    public static int? PerAnswer(long seconds, int attempts)
        => attempts <= 0 ? null : (int)Math.Round((double)seconds / attempts, MidpointRounding.AwayFromZero);

    /// <summary>Finished once every item is complete; in progress from the first answer.</summary>
    public static TakeStatus Status(IReadOnlyCollection<RoutineItemProgress> items)
    {
        if (items.Count > 0 && items.All(p => p.IsComplete)) return TakeStatus.Finished;
        return items.Any(p => p.Attempts > 0) ? TakeStatus.InProgress : TakeStatus.NotStarted;
    }

    /// <summary>
    /// A finished routine is late when its last question was answered after the due date, which only
    /// a routine taking late answers allows (otherwise it closed at the end of that day in the
    /// student's time zone). An unfinished one is late once the due date has passed. The student's
    /// time zone is not stored, so the teacher's stands in for it.
    /// </summary>
    public static bool IsLate(
        TakeStatus status, DateTime? finishedUtc, DateTime? dueAt, bool allowLate, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        if (dueAt is not { } due) return false;
        if (status == TakeStatus.Finished)
        {
            return allowLate && finishedUtc is { } finished
                && PracticeStreak.LocalDate(finished, timeZone) > DateOnly.FromDateTime(due);
        }
        return RoutineSchedule.Window(due, allowLate, nowUtc, timeZone) != RoutineWindow.Open;
    }

    /// <param name="items">In the routine's order; null where the item is excluded for the student.</param>
    /// <param name="lastAnswerUtc">When the student last answered one of the items.</param>
    public static RoutineTake Take(
        ReportStudent student,
        IReadOnlyList<TakeItem?> items,
        DateTime? lastAnswerUtc,
        DateTime? dueAt,
        bool allowLate,
        DateTime nowUtc,
        TimeZoneInfo timeZone)
    {
        var status = Status(items.OfType<TakeItem>().Select(i => i.Progress).ToList());
        var finishedUtc = status == TakeStatus.Finished ? lastAnswerUtc : null;
        return new RoutineTake(
            student,
            items,
            status,
            IsLate: IsLate(status, finishedUtc, dueAt, allowLate, nowUtc, timeZone),
            FinishedAt: finishedUtc is { } finished ? PracticeStreak.LocalTime(finished, timeZone) : null,
            LastAnswerAt: lastAnswerUtc is { } last ? PracticeStreak.LocalTime(last, timeZone) : null);
    }
}
