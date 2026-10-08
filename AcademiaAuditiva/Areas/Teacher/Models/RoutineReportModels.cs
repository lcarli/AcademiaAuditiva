using AcademiaAuditiva.Areas.Teacher.Services;

namespace AcademiaAuditiva.Areas.Teacher.Models;

/// <summary>A student as the reports show them: what the privacy policy says their teachers see.</summary>
public sealed record ReportStudent(string Id, string UserName, string Email)
{
    /// <summary>Whether the email needs its own line: accounts made on the site use it as their user name.</summary>
    public bool ShowsEmail => Email.Length > 0 && !string.Equals(Email, UserName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One assignment of one of the teacher's routines.</summary>
/// <param name="ClassroomName">Set when the routine is assigned to a classroom.</param>
/// <param name="StudentName">Set when the routine is assigned to one student.</param>
/// <param name="AssignedOn">In the teacher's time zone.</param>
/// <param name="ChosenStudents">How many students the teacher ticked, when the routine went to only some of the classroom.</param>
public sealed record AssignmentSummary(
    int Id,
    int RoutineId,
    string RoutineName,
    int? ClassroomId,
    string? ClassroomName,
    string? StudentName,
    DateOnly AssignedOn,
    DateOnly? DueOn,
    bool AllowLate,
    int? ChosenStudents = null);

/// <summary>An item of a routine, numbered from 1, with the target the teacher set for everyone.</summary>
/// <param name="ExerciseName">The exercise key, which the views localize.</param>
public sealed record ReportItem(int Id, int Number, string ExerciseName, int Target, int? MinScore);

public enum TakeStatus
{
    NotStarted,
    InProgress,
    Finished
}

/// <summary>A student's answers to one item, against their own target once their override is applied.</summary>
/// <param name="Seconds">The time spent on all its answers.</param>
public sealed record TakeItem(RoutineItemProgress Progress, long Seconds)
{
    public int? SecondsPerAnswer => RoutineReportRules.PerAnswer(Seconds, Progress.Attempts);
}

/// <summary>A student's take of an assigned routine, counted from the answers given in it only.</summary>
/// <param name="Items">In the routine's order (<see cref="ReportItem.Number"/>); null where the item is excluded for the student.</param>
/// <param name="IsLate">Finished after the due date, or not finished once it has passed (<see cref="RoutineReportRules.IsLate"/>).</param>
/// <param name="FinishedAt">When the last question was answered, in the teacher's time zone.</param>
/// <param name="LastAnswerAt">In the teacher's time zone.</param>
public sealed record RoutineTake(
    ReportStudent Student,
    IReadOnlyList<TakeItem?> Items,
    TakeStatus Status,
    bool IsLate,
    DateTime? FinishedAt,
    DateTime? LastAnswerAt)
{
    public int Attempts => Items.Sum(i => i?.Progress.Attempts ?? 0);
    public int Correct => Items.Sum(i => i?.Progress.Correct ?? 0);

    /// <summary>Questions answered, out of <see cref="Questions"/>.</summary>
    public int Answered => Items.Sum(i => i?.Progress.Done ?? 0);
    public int Questions => Items.Sum(i => i is null ? 0 : Math.Max(i.Progress.Target, 0));

    public int? Accuracy => RoutineReportRules.Percent(Correct, Attempts);
    public int? SecondsPerAnswer => RoutineReportRules.PerAnswer(Items.Sum(i => i?.Seconds ?? 0), Attempts);
}

/// <summary>How a set of takes is going: one assignment's, or one student's.</summary>
/// <param name="Takes">Students taking an assignment, or routines a student takes.</param>
/// <param name="Late">Takes that are <see cref="RoutineTake.IsLate"/>.</param>
public sealed record TakeTotals(int Takes, int Finished, int InProgress, int NotStarted, int Late, int Attempts, int Correct)
{
    /// <summary>Right answers out of all answers, so each answer weighs the same.</summary>
    public int? Accuracy => RoutineReportRules.Percent(Correct, Attempts);

    public static TakeTotals Of(IEnumerable<RoutineTake> takes)
    {
        var list = takes as IReadOnlyCollection<RoutineTake> ?? takes.ToList();
        return new TakeTotals(
            Takes: list.Count,
            Finished: list.Count(t => t.Status == TakeStatus.Finished),
            InProgress: list.Count(t => t.Status == TakeStatus.InProgress),
            NotStarted: list.Count(t => t.Status == TakeStatus.NotStarted),
            Late: list.Count(t => t.IsLate),
            Attempts: list.Sum(t => t.Attempts),
            Correct: list.Sum(t => t.Correct));
    }

    public static TakeTotals Sum(IEnumerable<TakeTotals> totals)
    {
        var list = totals as IReadOnlyCollection<TakeTotals> ?? totals.ToList();
        return new TakeTotals(
            Takes: list.Sum(t => t.Takes),
            Finished: list.Sum(t => t.Finished),
            InProgress: list.Sum(t => t.InProgress),
            NotStarted: list.Sum(t => t.NotStarted),
            Late: list.Sum(t => t.Late),
            Attempts: list.Sum(t => t.Attempts),
            Correct: list.Sum(t => t.Correct));
    }
}

/// <summary>How the students taking an assignment did on one of its items.</summary>
/// <param name="Students">Students who have the item: it is not excluded for them.</param>
/// <param name="Completed">Of those, the ones who answered all its questions.</param>
public sealed record ItemSummary(ReportItem Item, int Students, int Completed, int Attempts, int Correct, long Seconds)
{
    public int? Accuracy => RoutineReportRules.Percent(Correct, Attempts);
    public int? SecondsPerAnswer => RoutineReportRules.PerAnswer(Seconds, Attempts);
}

/// <summary>An assignment, item by item and student by student.</summary>
/// <param name="Takes">By user name.</param>
public sealed record AssignmentReport(AssignmentSummary Assignment, IReadOnlyList<ItemSummary> Items, IReadOnlyList<RoutineTake> Takes)
{
    public TakeTotals Totals => TakeTotals.Of(Takes);
}

public sealed record ClassroomRoutineRow(AssignmentSummary Assignment, TakeTotals Totals);

/// <param name="Totals">The student's takes of the routines in the report.</param>
/// <param name="LastAnswerAt">In the teacher's time zone.</param>
public sealed record ClassroomStudentRow(ReportStudent Student, TakeTotals Totals, DateTime? LastAnswerAt);

/// <summary>
/// The routines assigned to a classroom or to some of its students, and how each of its
/// students is doing in them.
/// </summary>
/// <param name="Routines">The latest assigned first.</param>
/// <param name="Students">Every current member, by user name.</param>
public sealed record ClassroomReport(int Id, string Name, IReadOnlyList<ClassroomRoutineRow> Routines, IReadOnlyList<ClassroomStudentRow> Students)
{
    public TakeTotals Totals => TakeTotals.Sum(Routines.Select(r => r.Totals));
}

public sealed record StudentRoutine(AssignmentSummary Assignment, IReadOnlyList<ReportItem> Items, RoutineTake Take);

/// <summary>A student's takes of the teacher's routines.</summary>
/// <param name="Classrooms">The teacher's classrooms the student is in.</param>
/// <param name="Routines">The latest assigned first.</param>
public sealed record StudentReport(ReportStudent Student, IReadOnlyList<ClassroomOption> Classrooms, IReadOnlyList<StudentRoutine> Routines)
{
    public TakeTotals Totals => TakeTotals.Of(Routines.Select(r => r.Take));
    public DateTime? LastAnswerAt => Routines.Max(r => r.Take.LastAnswerAt);
}

/// <summary>The student report, and the report the teacher came from, if it is one of the student's.</summary>
public sealed record StudentReportPage(StudentReport Report, int? BackToAssignmentId, int? BackToClassroomId);
