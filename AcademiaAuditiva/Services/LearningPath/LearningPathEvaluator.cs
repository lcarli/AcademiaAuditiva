using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.Services.LearningPath;

public enum StepState
{
    Locked,
    Current,
    Completed,
}

/// <summary>Where the player stands on one step.</summary>
/// <param name="Correct">Right answers among the last <see cref="PathStep.Window"/> answers that count for the step.</param>
/// <param name="Answered">Answers in that window (at most <see cref="PathStep.Window"/>).</param>
/// <param name="Placed">Completed by the placement test rather than by answers.</param>
public sealed record StepStatus(PathStep Step, StepState State, int Correct, int Answered, bool Placed = false)
{
    public int Percent => State switch
    {
        StepState.Completed => 100,
        StepState.Current => Math.Min(99, Correct * 100 / Step.Required),
        _ => 0,
    };
}

/// <param name="JustCompleted">Index of the step the latest answer completed, if it completed one.</param>
public sealed record PathEvaluation(IReadOnlyList<StepStatus> Steps, int? JustCompleted);

public static class LearningPathEvaluator
{
    /// <summary>
    /// Walks the steps in order. A step only counts the answers on its exercise given
    /// after the answer that completed the previous step, and is complete at the first
    /// answer where <see cref="PathStep.Required"/> of the last <see cref="PathStep.Window"/>
    /// are right. So one answer completes at most one step, and every step after the
    /// first incomplete one is locked. The first <paramref name="placedSteps"/> steps,
    /// which the placement test let the player skip, are complete anyway: those the answers
    /// don't complete are marked <see cref="StepStatus.Placed"/>, and the next step counts
    /// answers from the same point as they did.
    /// </summary>
    /// <param name="steps">Steps whose exercise is in <paramref name="exerciseIds"/>.</param>
    /// <param name="exerciseIds">Exercise.Name → ExerciseId.</param>
    /// <param name="answers">The player's answers, oldest first.</param>
    /// <param name="placedSteps">How many of the first steps the placement test completed.</param>
    public static PathEvaluation Evaluate(
        IReadOnlyList<PathStep> steps,
        IReadOnlyDictionary<string, int> exerciseIds,
        IReadOnlyList<PracticeAnswer> answers,
        int placedSteps = 0)
    {
        var statuses = new List<StepStatus>(steps.Count);
        int? justCompleted = null;
        var from = 0;
        var unlocked = true;

        foreach (var step in steps)
        {
            if (!unlocked)
            {
                statuses.Add(new StepStatus(step, StepState.Locked, 0, 0));
                continue;
            }

            var exerciseId = exerciseIds[step.Exercise];
            var window = new Queue<bool>(step.Window + 1);
            var correct = 0;
            var completedBy = -1;
            for (var i = from; i < answers.Count; i++)
            {
                if (answers[i].ExerciseId != exerciseId) continue;

                window.Enqueue(answers[i].IsCorrect);
                if (answers[i].IsCorrect) correct++;
                if (window.Count > step.Window && window.Dequeue()) correct--;
                if (correct >= step.Required)
                {
                    completedBy = i;
                    break;
                }
            }

            if (completedBy < 0)
            {
                if (statuses.Count < placedSteps)
                {
                    statuses.Add(new StepStatus(step, StepState.Completed, correct, window.Count, Placed: true));
                    continue;
                }

                statuses.Add(new StepStatus(step, StepState.Current, correct, window.Count));
                unlocked = false;
                continue;
            }

            statuses.Add(new StepStatus(step, StepState.Completed, correct, window.Count));
            if (completedBy == answers.Count - 1) justCompleted = statuses.Count - 1;
            from = completedBy + 1;
        }

        return new PathEvaluation(statuses, justCompleted);
    }
}
