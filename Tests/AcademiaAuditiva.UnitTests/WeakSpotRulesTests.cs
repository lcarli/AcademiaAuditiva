using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Games;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// A weak spot is an exercise, with given settings, answered at least ten times with fewer than
/// 80 % of the last twenty answers right.
/// </summary>
public class WeakSpotRulesTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Easy = """{"level":"easy"}""";
    private const string Hard = """{"level":"hard"}""";

    [Fact]
    public void Exercise_IsAWeakSpot_OnlyAfterTenAnswers()
    {
        Find(Answers(1, null, right: 0, wrong: 9)).Should().BeEmpty("nine answers say too little");

        var spot = Find(Answers(1, null, right: 0, wrong: 10)).Should().ContainSingle().Subject;
        spot.Should().Be(new WeakSpot(1, null, 0, 10));
        spot.Percent.Should().Be(0);
    }

    [Fact]
    public void Exercise_IsAWeakSpot_BelowEightyPercent()
    {
        Find(Answers(1, null, right: 8, wrong: 2)).Should().BeEmpty("80 % is good enough");

        var spot = Find(Answers(1, null, right: 7, wrong: 3)).Should().ContainSingle().Subject;
        spot.Percent.Should().Be(70);
    }

    [Fact]
    public void OnlyTheLastTwentyAnswers_Count_SoOldMistakesFade()
    {
        var answers = Answers(1, null, right: 0, wrong: 10).Concat(Answers(1, null, right: 20, wrong: 0, from: 10)).ToList();
        Find(answers).Should().BeEmpty();

        answers = Answers(1, null, right: 20, wrong: 0).Concat(Answers(1, null, right: 0, wrong: 6, from: 20)).ToList();
        Find(answers).Should().ContainSingle().Which.Should().Be(new WeakSpot(1, null, 14, 20));
    }

    [Fact]
    public void EachSetting_IsItsOwnSpot()
    {
        var answers = Answers(1, Easy, right: 10, wrong: 0)
            .Concat(Answers(1, Hard, right: 3, wrong: 7, from: 10))
            .ToList();

        Find(answers).Should().ContainSingle().Which.FilterJson.Should().Be(Hard);
    }

    [Fact]
    public void Spots_AreWeakestFirst_AtMostFive()
    {
        var answers = new List<PracticeAnswer>();
        for (var id = 1; id <= 7; id++)
            answers.AddRange(Answers(id, null, right: id - 1, wrong: 11 - id, from: answers.Count));

        Find(answers).Select(s => s.ExerciseId).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Exercises_ThatCantBePlayed_AreLeftOut()
    {
        var answers = Answers(1, null, right: 0, wrong: 10).Concat(Answers(2, null, right: 0, wrong: 10, from: 10)).ToList();

        WeakSpotRules.Find(answers, id => id != 1).Select(s => s.ExerciseId).Should().Equal(2);
    }

    private static IReadOnlyList<WeakSpot> Find(IReadOnlyList<PracticeAnswer> answers) => WeakSpotRules.Find(answers, _ => true);

    private static List<PracticeAnswer> Answers(int exerciseId, string? filterJson, int right, int wrong, int from = 0)
        => Enumerable.Range(0, right + wrong)
            .Select(i => new PracticeAnswer(exerciseId, i < right, Start.AddMinutes(from + i), filterJson))
            .ToList();
}
