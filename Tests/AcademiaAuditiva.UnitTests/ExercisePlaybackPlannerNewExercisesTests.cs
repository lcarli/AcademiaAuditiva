using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.UnitTests;

public class ExercisePlaybackPlannerNewExercisesTests
{
    private static Exercise Exercise(string name) => new() { ExerciseId = 800, Name = name };

    [Fact]
    public void HigherOrLower_UsesSequentialTwoNotePlan()
    {
        var plan = new ExercisePlaybackPlanner().Plan(
            Exercise("HigherOrLower"),
            new Dictionary<string, string> { ["noteRange"] = "C4-C4" });

        plan.PlaybackPlans.Should().HaveCount(1);
        plan.PlaybackPlans[0].Should().HaveCount(2);
        plan.PlaybackPlans[0][1].StartTimeSeconds.Should()
            .BeGreaterThan(plan.PlaybackPlans[0][0].StartTimeSeconds);
    }

    [Theory]
    [InlineData("GuessScaleType")]
    [InlineData("GuessGreekMode")]
    public void ScaleListeningExercises_PlayGeneratedScaleInSequence(string exerciseName)
    {
        var plan = new ExercisePlaybackPlanner().Plan(Exercise(exerciseName), new Dictionary<string, string>());

        plan.PlaybackPlans.Should().HaveCount(1);
        plan.PlaybackPlans[0].Count.Should().BeGreaterThan(1);

        var starts = plan.PlaybackPlans[0].Select(i => i.StartTimeSeconds).ToArray();
        starts.Should().BeInAscendingOrder();
        starts.Distinct().Should().HaveCount(starts.Length);
    }

    [Fact]
    public void GuessInversion_PlaysChordNotesAtOnce()
    {
        var plan = new ExercisePlaybackPlanner().Plan(
            Exercise("GuessInversion"),
            new Dictionary<string, string> { ["invQuality"] = "major", ["invOctave"] = "4" });

        plan.PlaybackPlans.Should().HaveCount(1);
        plan.PlaybackPlans[0].Should().HaveCount(3);
        plan.PlaybackPlans[0].Select(i => i.StartTimeSeconds).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void CompleteScale_ReturnsMetadataSafeStaffAudioPlan()
    {
        var plan = new ExercisePlaybackPlanner().Plan(
            Exercise("CompleteScale"),
            new Dictionary<string, string>
            {
                ["csRoot"] = "C",
                ["csScale"] = "major",
                ["csOctave"] = "4"
            });

        plan.PlaybackPlans.Should().HaveCount(1);
        plan.PlaybackPlans[0].Should().ContainSingle();
        plan.ExpectedAnswerJson.Should().Contain("\"promptNotes\"");
        plan.ExpectedAnswerJson.Should().Contain("\"answerString\"");
    }
}
