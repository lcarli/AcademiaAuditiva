using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>
/// Where the student stands on a routine item: the exercise page's routine banner
/// (Views/Exercise/_RoutineBanner) shows it, and RequestPlay and ValidateExercise
/// return it in their <c>routine</c> field so wwwroot/js/core/practice.js keeps the banner in step.
/// </summary>
/// <param name="State"><c>open</c>, <c>late</c> (past the due date, still taking answers),
/// <c>closed</c> (past the due date), <c>done</c> (every question answered) or <c>missing</c>
/// (not, or no longer, the student's routine).</param>
/// <param name="Text">"Question 3 of 10", the result once done, or why it takes no answers.</param>
/// <param name="Verdict">Once done, whether the minimum accuracy was reached; null without a minimum.</param>
/// <param name="Blocked">Why Play takes no more questions; null while it still does.</param>
/// <param name="Percent">The item's progress, see <see cref="Areas.Teacher.Services.RoutineItemProgress.Percent"/>.</param>
public sealed record RoutineRoundStatus(
    string State,
    string Text,
    string? Verdict,
    bool? Passed,
    RoutineBlock? Blocked,
    int Answered,
    int Correct,
    int Target,
    int Percent)
{
    public const string Open = "open";
    public const string Late = "late";
    public const string Closed = "closed";
    public const string Done = "done";
    public const string Missing = "missing";

    public static RoutineRoundStatus From(AssignedRoutineItem item, IStringLocalizer localizer)
    {
        var progress = item.Progress;
        if (progress.IsComplete)
        {
            bool? passed = progress.MinScore is null ? null : progress.MeetsMinScore;
            return new RoutineRoundStatus(
                State: Done,
                Text: localizer["Routine.Result", progress.Correct, progress.Attempts, progress.Accuracy ?? 0].Value,
                Verdict: passed switch
                {
                    true => localizer["Routine.Passed", progress.MinScore!].Value,
                    false => localizer["Routine.BelowMin", progress.MinScore!].Value,
                    null => null
                },
                Passed: passed,
                Blocked: new RoutineBlock(localizer["Routine.ItemDoneTitle"].Value, localizer["Routine.ItemDone"].Value),
                Answered: progress.Attempts,
                Correct: progress.Correct,
                Target: progress.Target,
                Percent: progress.Percent);
        }

        if (item.Window == RoutineWindow.Closed)
        {
            var closed = localizer["Routine.Closed"].Value;
            return new RoutineRoundStatus(
                Closed, closed, null, null,
                new RoutineBlock(localizer["Routine.ClosedTitle"].Value, closed),
                progress.Attempts, progress.Correct, progress.Target, progress.Percent);
        }

        return new RoutineRoundStatus(
            State: item.Window == RoutineWindow.Late ? Late : Open,
            Text: localizer["Routine.Question", item.NextQuestion, progress.Target].Value,
            Verdict: null,
            Passed: null,
            Blocked: null,
            Answered: progress.Attempts,
            Correct: progress.Correct,
            Target: progress.Target,
            Percent: progress.Percent);
    }

    /// <summary>The routine item is not, or no longer, the student's (see <see cref="RoutineRounds.FindAsync"/>).</summary>
    public static RoutineRoundStatus Unavailable(IStringLocalizer localizer)
    {
        var message = localizer["Routine.Unavailable"].Value;
        return new RoutineRoundStatus(
            Missing, message, null, null,
            new RoutineBlock(localizer["Routine.UnavailableTitle"].Value, message),
            0, 0, 0, 0);
    }
}

/// <summary>Why Play takes no more questions, as the title and message of a dialog.</summary>
public sealed record RoutineBlock(string Title, string Message);
