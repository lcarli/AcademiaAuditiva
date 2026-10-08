using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Games;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// A sprint lasts a minute and still takes the answer on its way when it ends; survival ends
/// at the first wrong answer, a weak spot run after ten; any run left open expires.
/// </summary>
public class GameRulesTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(GameModes.Sprint, 1)]
    [InlineData(GameModes.Survival, 120)]
    [InlineData(GameModes.WeakSpots, 120)]
    [InlineData(GameModes.Placement, 24 * 60)]
    public void Run_TakesRounds_UntilItsDeadline(string mode, int minutes)
    {
        var run = Run(mode);

        GameRules.Deadline(run).Should().Be(Start.AddMinutes(minutes));
        GameRules.IsOpen(run, Start).Should().BeTrue();
        GameRules.IsOpen(run, Start.AddMinutes(minutes).AddTicks(-1)).Should().BeTrue();
        GameRules.IsOpen(run, Start.AddMinutes(minutes)).Should().BeFalse();
        GameRules.IsOver(run, 0, 0, Start.AddMinutes(minutes)).Should().BeTrue("a run left open expires");

        run.EndedAt = Start.AddSeconds(5);
        GameRules.IsOpen(run, Start.AddSeconds(6)).Should().BeFalse("an ended run takes no round");
        GameRules.IsOver(run, 0, 0, Start.AddSeconds(6)).Should().BeTrue();
    }

    [Fact]
    public void Sprint_TakesTheAnswerOnItsWay_ForAFewSecondsAfterTheWhistle()
    {
        var run = Run(GameModes.Sprint);
        var whistle = Start + GameRules.SprintLength;

        GameRules.TakesAnswer(run, whistle).Should().BeTrue();
        GameRules.TakesAnswer(run, whistle + GameRules.SprintGrace).Should().BeTrue();
        GameRules.TakesAnswer(run, whistle + GameRules.SprintGrace + TimeSpan.FromMilliseconds(1)).Should().BeFalse();
    }

    [Fact]
    public void StoppedSprint_TakesTheAnswerOnItsWay_ForAFewSecondsAfterTheStop()
    {
        var run = Run(GameModes.Sprint);
        run.EndedAt = Start.AddSeconds(20);

        GameRules.TakesAnswer(run, Start.AddSeconds(23)).Should().BeTrue();
        GameRules.TakesAnswer(run, Start.AddSeconds(24)).Should().BeFalse("the player stopped at 20 s");
        GameRules.SecondsLeft(run, Start.AddSeconds(21)).Should().Be(0);
    }

    [Theory]
    [InlineData(GameModes.Survival)]
    [InlineData(GameModes.WeakSpots)]
    [InlineData(GameModes.Placement)]
    public void OtherModes_TakeAnswers_OnlyWhileOpen(string mode)
    {
        var run = Run(mode);

        GameRules.TakesAnswer(run, Start.AddMinutes(5)).Should().BeTrue();
        run.EndedAt = Start.AddMinutes(5);
        GameRules.TakesAnswer(run, Start.AddMinutes(5)).Should().BeFalse();
    }

    [Fact]
    public void Survival_IsOver_AtTheFirstWrongAnswer()
    {
        var run = Run(GameModes.Survival);

        GameRules.IsOver(run, score: 12, answered: 12, Start.AddMinutes(10)).Should().BeFalse();
        GameRules.IsOver(run, score: 12, answered: 13, Start.AddMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void WeakSpots_IsOver_AfterTenAnswers()
    {
        var run = Run(GameModes.WeakSpots);

        GameRules.IsOver(run, score: 0, answered: GameRules.WeakSpotQuestions - 1, Start).Should().BeFalse();
        GameRules.IsOver(run, score: 0, answered: GameRules.WeakSpotQuestions, Start).Should().BeTrue();
    }

    [Fact]
    public void Sprint_IsOver_OnlyWhenTheTimeIsUp_WhateverTheAnswers()
    {
        var run = Run(GameModes.Sprint);

        GameRules.IsOver(run, score: 0, answered: 40, Start.AddSeconds(59)).Should().BeFalse();
        GameRules.IsOver(run, score: 0, answered: 40, Start.AddSeconds(60)).Should().BeTrue();
    }

    [Fact]
    public void SecondsLeft_RoundsUp_AndIsOnlyForSprints()
    {
        var run = Run(GameModes.Sprint);

        GameRules.SecondsLeft(run, Start).Should().Be(60);
        GameRules.SecondsLeft(run, Start.AddSeconds(0.1)).Should().Be(60);
        GameRules.SecondsLeft(run, Start.AddSeconds(59.5)).Should().Be(1);
        GameRules.SecondsLeft(run, Start.AddSeconds(60)).Should().Be(0);
        GameRules.SecondsLeft(run, Start.AddSeconds(90)).Should().Be(0);
        GameRules.SecondsLeft(Run(GameModes.Survival), Start).Should().BeNull();
    }

    [Theory]
    [InlineData("sprint", GameModes.Sprint)]
    [InlineData(" Survival ", GameModes.Survival)]
    [InlineData("WEAKSPOTS", GameModes.WeakSpots)]
    [InlineData("placement", GameModes.Placement)]
    [InlineData("marathon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Mode_IsParsedFromTheQuery_IgnoringCase(string? value, string? mode)
        => GameModes.Parse(value).Should().Be(mode);

    [Fact]
    public void ExerciseModes_AreTheOnesPlayedOnOneExercise_AndOnlyTimedOrStreakModesKeepRecords()
    {
        GameModes.ExerciseModes.Should().Equal(GameModes.Sprint, GameModes.Survival, GameModes.WeakSpots);
        GameModes.IsExerciseMode(GameModes.Placement).Should().BeFalse();
        GameModes.RecordModes.Should().Equal(GameModes.Sprint, GameModes.Survival);
        GameModes.ExerciseModes.Concat([GameModes.Placement])
            .Should().OnlyContain(m => m.Length <= GameRun.ModeMaxLength, "the mode is stored in a short column");
    }

    [Theory]
    [InlineData("GuessNote", true)]
    [InlineData("GuessTuning", true)]
    [InlineData("SingNote", false)]
    [InlineData("SolfegeMelody", false)]
    public void SungExercises_AreNotPlayedAsGames(string exercise, bool playable)
        => GameModes.Playable(exercise).Should().Be(playable);

    [Theory]
    [InlineData(GameModes.Sprint, "Sprint", "bi-stopwatch")]
    [InlineData(GameModes.Survival, "Survival", "bi-heart-pulse")]
    [InlineData(GameModes.WeakSpots, "WeakSpots", "bi-bullseye")]
    [InlineData(GameModes.Placement, "Placement", "bi-speedometer2")]
    public void EveryMode_HasItsTextsAndIcon(string mode, string key, string icon)
    {
        GameDisplay.Key(mode).Should().Be(key);
        GameDisplay.Icon(mode).Should().Be(icon);
    }

    private static GameRun Run(string mode) => new() { Id = 1, UserId = "u", Mode = mode, StartedAt = Start };
}
