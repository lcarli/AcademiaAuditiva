using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;

namespace AcademiaAuditiva.UnitTests;

/// <summary>Step states on a small path: A (2 of the last 3), B (2 of 2), C (1 of 1).</summary>
public class LearningPathEvaluatorTests
{
    private static readonly IReadOnlyDictionary<string, string> NoFilters = new Dictionary<string, string>();
    private static readonly PathStep[] Steps =
    [
        new("A", NoFilters, Window: 3, Required: 2),
        new("B", NoFilters, Window: 2, Required: 2),
        new("C", NoFilters, Window: 1, Required: 1),
    ];
    private static readonly Dictionary<string, int> Ids = new() { ["A"] = 1, ["B"] = 2, ["C"] = 3 };
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutAnswers_TheFirstStepIsCurrent_AndTheOthersLocked()
    {
        var result = Evaluate();

        result.Steps.Select(s => s.State).Should().Equal(StepState.Current, StepState.Locked, StepState.Locked);
        result.Steps[0].Should().BeEquivalentTo(new { Correct = 0, Answered = 0, Percent = 0 });
        result.JustCompleted.Should().BeNull();
    }

    [Fact]
    public void Step_CountsTheLastWindowOfAnswers_AndLockedStepsCountNothing()
    {
        var result = Evaluate(("A", true), ("A", false), ("A", false), ("A", true), ("B", true));

        result.Steps[0].Should().BeEquivalentTo(new { State = StepState.Current, Correct = 1, Answered = 3, Percent = 50 });
        result.Steps[1].Should().BeEquivalentTo(new { State = StepState.Locked, Correct = 0, Answered = 0, Percent = 0 });
        result.JustCompleted.Should().BeNull();
    }

    [Fact]
    public void Step_CompletesAtTheFirstAnswerThatReachesItsGoal()
    {
        var result = Evaluate(("A", true), ("A", false), ("A", false), ("A", true), ("A", true));

        result.Steps[0].Should().BeEquivalentTo(new { State = StepState.Completed, Correct = 2, Percent = 100 });
        result.Steps[1].Should().BeEquivalentTo(new { State = StepState.Current, Correct = 0, Answered = 0 });
        result.Steps[2].State.Should().Be(StepState.Locked);
        result.JustCompleted.Should().Be(0);
    }

    [Fact]
    public void NextStep_OnlyCountsAnswersGivenAfterThePreviousStepWasCompleted()
    {
        var result = Evaluate(("B", true), ("B", true), ("A", true), ("A", true), ("B", true));

        result.Steps[0].State.Should().Be(StepState.Completed);
        result.Steps[1].Should().BeEquivalentTo(new { State = StepState.Current, Correct = 1, Answered = 1, Percent = 50 });
        result.JustCompleted.Should().BeNull("the latest answer did not complete a step");
    }

    [Fact]
    public void JustCompleted_IsTheStepTheLatestAnswerCompleted()
    {
        (string, bool)[] answers = [("A", true), ("A", true), ("B", true), ("B", true), ("C", true)];

        Evaluate(answers[..2]).JustCompleted.Should().Be(0);
        Evaluate(answers[..3]).JustCompleted.Should().BeNull();
        Evaluate(answers[..4]).JustCompleted.Should().Be(1);
        var all = Evaluate(answers);
        all.Steps.Should().OnlyContain(s => s.State == StepState.Completed && s.Percent == 100);
        all.JustCompleted.Should().Be(2);
    }

    [Fact]
    public void AnAnswerCompletesAtMostOneStep()
    {
        PathStep[] steps = [new("A", NoFilters, Window: 2, Required: 2), new("A", NoFilters, Window: 1, Required: 1)];

        var result = LearningPathEvaluator.Evaluate(steps, Ids, Answers(("A", true), ("A", true)));

        result.Steps.Select(s => s.State).Should().Equal(StepState.Completed, StepState.Current);
        result.JustCompleted.Should().Be(0);
    }

    private static PathEvaluation Evaluate(params (string Exercise, bool Correct)[] answers)
        => LearningPathEvaluator.Evaluate(Steps, Ids, Answers(answers));

    private static List<PracticeAnswer> Answers(params (string Exercise, bool Correct)[] answers)
        => answers.Select((a, i) => new PracticeAnswer(Ids[a.Exercise], a.Correct, Start.AddMinutes(i))).ToList();
}
