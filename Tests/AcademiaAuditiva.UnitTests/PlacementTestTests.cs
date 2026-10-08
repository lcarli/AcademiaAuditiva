using AcademiaAuditiva.Services.Games;
using AcademiaAuditiva.Services.LearningPath;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The placement test checks the first two learning path units with three of their steps,
/// each asked three times: 7 right answers of 9 pass a unit, 3 wrong ones fail it and end the
/// test there.
/// </summary>
public class PlacementTestTests
{
    [Fact]
    public void Items_AreLearningPathSteps_ThreePerUnit_InOrder()
    {
        PlacementTest.Items.Select(i => (i.Unit, i.Exercise)).Should().Equal(
            (1, "GuessInterval"), (1, "GuessChords"), (1, "GuessDegree"),
            (2, "GuessQuality"), (2, "GuessFunction"), (2, "GuessRhythmPattern"));

        foreach (var item in PlacementTest.Items)
            LearningPathCatalog.Units[item.Unit - 1].Steps.Should().Contain(item.Step, "{0} asks the step's preset", item.Exercise);
        PlacementTest.Items.Should().OnlyContain(i => GameModes.Playable(i.Exercise));
        PlacementTest.Items.Should().OnlyContain(i => PlacementTest.Asks(i.Exercise));
        PlacementTest.Asks("GuessNote").Should().BeFalse();
        PlacementTest.MaxUnit.Should().Be(3).And.BeLessThanOrEqualTo(LearningPathCatalog.Units.Count);
    }

    [Fact]
    public void Test_StartsOnTheFirstQuestionOfTheFirstItem()
    {
        var state = PlacementTest.Evaluate([]);

        state.Should().BeEquivalentTo(new { ItemIndex = 0, Question = 1, SuggestedUnit = (int?)null, Finished = false });
        state.Current!.Exercise.Should().Be("GuessInterval");
        state.Units.Should().Equal(new PlacementUnitResult(1, 0, 0, null), new PlacementUnitResult(2, 0, 0, null));
    }

    [Theory]
    [InlineData(1, 0, 2)]
    [InlineData(2, 0, 3)]
    [InlineData(3, 1, 1)]
    [InlineData(5, 1, 3)]
    [InlineData(8, 2, 3)]
    public void EachItem_IsAskedThreeTimes(int answered, int itemIndex, int question)
    {
        // Six right and two wrong answers don't decide the first unit yet.
        var state = PlacementTest.Evaluate(Answers("11111100").Take(answered).ToList());

        state.ItemIndex.Should().Be(itemIndex);
        state.Question.Should().Be(question);
    }

    [Fact]
    public void SevenRightAnswers_PassTheUnit_AndMoveOnToTheNext()
    {
        var state = PlacementTest.Evaluate(Answers("1111111"));

        state.Finished.Should().BeFalse();
        state.Current!.Exercise.Should().Be("GuessQuality");
        state.Question.Should().Be(1);
        state.Units.Should().Equal(new PlacementUnitResult(1, 7, 7, true), new PlacementUnitResult(2, 0, 0, null));
    }

    [Fact]
    public void ThreeWrongAnswers_FailTheUnit_AndSuggestIt()
    {
        PlacementTest.Evaluate(Answers("1010")).Finished.Should().BeFalse("7 right answers are still possible");

        var state = PlacementTest.Evaluate(Answers("10100"));

        state.Should().BeEquivalentTo(new { ItemIndex = -1, Question = 0, SuggestedUnit = (int?)1, Finished = true });
        state.Current.Should().BeNull();
        state.Units.Should().Equal(new PlacementUnitResult(1, 2, 5, false), new PlacementUnitResult(2, 0, 0, null));
    }

    [Fact]
    public void PassingTheFirstUnit_AndFailingTheSecond_SuggestsTheSecond()
    {
        var state = PlacementTest.Evaluate(Answers("11011111" + "000"));

        state.SuggestedUnit.Should().Be(2);
        state.Units.Should().Equal(new PlacementUnitResult(1, 7, 8, true), new PlacementUnitResult(2, 0, 3, false));
    }

    [Fact]
    public void PassingEveryUnit_SuggestsTheNextOne()
    {
        var state = PlacementTest.Evaluate(Answers("1111111" + "011111101"));

        state.SuggestedUnit.Should().Be(PlacementTest.MaxUnit);
        state.Units.Should().Equal(new PlacementUnitResult(1, 7, 7, true), new PlacementUnitResult(2, 7, 9, true));
    }

    [Fact]
    public void AUnit_TakesAtMostNineAnswers()
    {
        for (var mask = 0; mask < 1 << PlacementTest.QuestionsPerUnit; mask++)
        {
            var answers = Enumerable.Range(0, PlacementTest.QuestionsPerUnit).Select(i => (mask >> i & 1) == 1).ToList();
            var unit = PlacementTest.Evaluate(answers).Units[0];

            unit.Passed.Should().NotBeNull("nine answers always decide a unit");
            unit.Answered.Should().BeLessThanOrEqualTo(PlacementTest.QuestionsPerUnit);
        }
    }

    private static List<bool> Answers(string pattern) => pattern.Select(c => c == '1').ToList();
}
