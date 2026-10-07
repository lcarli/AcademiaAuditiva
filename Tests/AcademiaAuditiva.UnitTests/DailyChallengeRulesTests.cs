using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services.DailyChallenge;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

public class DailyChallengeRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 5);
    private static readonly DateTime Noon = Day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
    private static readonly Lazy<IReadOnlyList<ChallengeExercise>> Seeded = new(LoadSeededExercises);

    /// <summary>One exercise per category, so every date draws all three.</summary>
    private static readonly ChallengeExercise[] Three =
    [
        new(1, "GuessNote", "EarTraining"),
        new(2, "GuessChords", "Harmony"),
        new(3, "RhythmDictation", "Rhythm"),
    ];

    [Fact]
    public void SeededExercises_DrawThreeCategoriesEveryDay_AndEveryExerciseRegularly()
    {
        var exercises = Seeded.Value;
        exercises.Should().Contain(e => e.Name == "SolfegeMelody");
        var drawn = new Dictionary<string, int>(StringComparer.Ordinal);
        var challenges = new HashSet<string>(StringComparer.Ordinal);
        var days = 0;

        for (var date = new DateOnly(2026, 1, 1); date.Year == 2026; date = date.AddDays(1), days++)
        {
            var picked = DailyChallengeRules.Pick(date, exercises);

            picked.Should().HaveCount(DailyChallengeRules.ExercisesPerDay);
            picked.Select(e => e.Category).Should().OnlyHaveUniqueItems($"{date} draws from different categories");
            foreach (var exercise in picked) drawn[exercise.Name] = drawn.GetValueOrDefault(exercise.Name) + 1;
            challenges.Add(string.Join(",", picked.Select(e => e.Name).Order(StringComparer.Ordinal)));
        }

        drawn.Keys.Should().BeEquivalentTo(
            exercises.Select(e => e.Name).Where(n => n != "SolfegeMelody").Distinct(),
            "every exercise but Solfege Melody, which needs a microphone, gets drawn");
        // The draw goes by category, so an exercise of a big category comes up less often than one
        // of a small category: each is drawn at least half, and at most twice, as often as it
        // would be if every exercise were drawn alike.
        var share = (double)days * DailyChallengeRules.ExercisesPerDay / drawn.Count;
        drawn.Should().AllSatisfy(kv => kv.Value.Should().BeInRange((int)(share / 2), (int)(share * 2),
            $"{kv.Key} is drawn about as often as the other exercises"));
        challenges.Count.Should().BeGreaterThan(days / 2, "the challenge changes from day to day");
    }

    [Fact]
    public void Pick_IsTheSameForTheSameDate_WhateverTheOrderOfTheRows()
    {
        var exercises = Seeded.Value;

        var picked = DailyChallengeRules.Pick(Day, exercises);

        DailyChallengeRules.Pick(Day, exercises.Reverse().ToList()).Should().Equal(picked);
        DailyChallengeRules.Pick(Day, exercises.OrderBy(e => e.Category).ThenByDescending(e => e.Name).ToList()).Should().Equal(picked);
    }

    [Fact]
    public void Pick_KeepsTheLowestIdOfARepeatedName()
    {
        ChallengeExercise[] exercises = [new(7, "GuessNote", "EarTraining"), new(3, "GuessNote", "EarTraining")];

        DailyChallengeRules.Pick(Day, exercises).Should().Equal(new ChallengeExercise(3, "GuessNote", "EarTraining"));
    }

    [Fact]
    public void Pick_WithFewerCategoriesThanExercises_FillsTheDayWithOtherExercises()
    {
        ChallengeExercise[] exercises =
        [
            new(1, "GuessNote", "EarTraining"),
            new(2, "GuessInterval", "EarTraining"),
            new(3, "GuessChords", "Harmony"),
            new(4, "GuessCadence", "Harmony"),
        ];

        var picked = DailyChallengeRules.Pick(Day, exercises);

        picked.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        picked.Take(2).Select(e => e.Category).Should().BeEquivalentTo(["EarTraining", "Harmony"], "each category comes first once");
    }

    [Fact]
    public void Pick_GivesTheExercisesOfACategoryInTurn_DayAfterDay()
    {
        ChallengeExercise[] exercises =
        [
            new(1, "GuessNote", "EarTraining"),
            new(2, "HigherOrLower", "EarTraining"),
            new(3, "GuessInterval", "EarTraining"),
            new(4, "GuessChords", "Harmony"),
            new(5, "RhythmDictation", "Rhythm"),
        ];

        var earTraining = Enumerable.Range(0, 30)
            .Select(i => DailyChallengeRules.Pick(Day.AddDays(i), exercises).Single(e => e.Category == "EarTraining").Name)
            .ToList();

        earTraining.Zip(earTraining.Skip(1)).Should().OnlyContain(days => days.First != days.Second,
            "an exercise doesn't come up two days in a row");
        earTraining.CountBy(name => name).ToDictionary().Should().BeEquivalentTo(
            new Dictionary<string, int> { ["GuessInterval"] = 10, ["GuessNote"] = 10, ["HigherOrLower"] = 10 },
            "each exercise of the category takes its turn");
    }

    [Fact]
    public void Pick_WithFewerExercises_TakesThemAll()
    {
        DailyChallengeRules.Pick(Day, []).Should().BeEmpty();
        DailyChallengeRules.Pick(Day, Three.Take(2)).Should().BeEquivalentTo(Three.Take(2));
        DailyChallengeRules.Pick(Day, [new(9, "SolfegeMelody", "Melody")]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("GuessNote", 5)]
    [InlineData("GuessChords", 5)]
    [InlineData("IntervalMelodico", 5)]
    [InlineData("CompleteChord", 3)]
    [InlineData("CompleteScale", 3)]
    [InlineData("MelodicDictation", 3)]
    [InlineData("RhythmDictation", 3)]
    [InlineData("TransposeScale", 3)]
    [InlineData("GuessRhythmPattern", 5)]
    [InlineData("RhythmTap", 5)]
    [InlineData("GuessTopNote", 5)]
    public void Target_IsLowerForTheExercisesAnsweredOnAStaff(string exercise, int target) =>
        DailyChallengeRules.Target(exercise).Should().Be(target);

    [Fact]
    public void Evaluate_CountsTheDaysAnswers_RightOrWrong_UpToTheTarget()
    {
        List<PracticeAnswer> answers =
        [
            .. Enumerable.Range(0, 7).Select(i => new PracticeAnswer(1, i % 2 == 0, Noon.AddMinutes(i))),
            new(2, false, Noon),
            new(3, true, Noon.AddDays(-1)),
            new(99, true, Noon),
        ];

        var progress = DailyChallengeRules.Evaluate(Day, Three, answers, TimeZoneInfo.Utc);

        progress.Date.Should().Be(Day);
        progress.Items.Should().BeEquivalentTo(new[]
        {
            new ChallengeItem("GuessNote", "EarTraining", 5, 5),
            new ChallengeItem("GuessChords", "Harmony", 1, 5),
            new ChallengeItem("RhythmDictation", "Rhythm", 0, 3),
        });
        progress.Items.Select(i => (i.Exercise, i.Done, i.Percent)).Should().BeEquivalentTo(new[]
        {
            ("GuessNote", true, 100),
            ("GuessChords", false, 20),
            ("RhythmDictation", false, 0),
        });
        progress.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_IsCompleteOnceEveryExerciseReachesItsTarget()
    {
        var answers = Three
            .SelectMany(e => Enumerable.Range(0, DailyChallengeRules.Target(e.Name))
                .Select(i => new PracticeAnswer(e.ExerciseId, false, Noon.AddMinutes(i))))
            .ToList();

        DailyChallengeRules.Evaluate(Day, Three, answers, TimeZoneInfo.Utc).IsComplete.Should().BeTrue();
        DailyChallengeRules.Evaluate(Day, Three, answers.Skip(1), TimeZoneInfo.Utc).IsComplete.Should().BeFalse();
        DailyChallengeRules.Evaluate(Day, [], answers, TimeZoneInfo.Utc).IsComplete.Should().BeFalse("there is nothing to complete");
    }

    [Fact]
    public void Evaluate_CountsAnswersOnThePlayersCalendarDay()
    {
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        // Oct 6, 02:00 UTC is still Oct 5, 22:00 in Toronto.
        PracticeAnswer[] answers = [new(1, true, new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc))];

        GuessNoteAnswers(DailyChallengeRules.Evaluate(Day, Three, answers, toronto)).Should().Be(1);
        GuessNoteAnswers(DailyChallengeRules.Evaluate(Day, Three, answers, TimeZoneInfo.Utc)).Should().Be(0);
        GuessNoteAnswers(DailyChallengeRules.Evaluate(Day.AddDays(1), Three, answers, TimeZoneInfo.Utc)).Should().Be(1);

        static int GuessNoteAnswers(DailyChallengeProgress progress) =>
            progress.Items.Single(i => i.Exercise == "GuessNote").Answered;
    }

    [Fact]
    public void Evaluate_CountsAnswersSavedUnderAnyRowOfTheName()
    {
        ChallengeExercise[] exercises = [.. Three, new(40, "GuessNote", "EarTraining")];
        PracticeAnswer[] answers = [new(1, true, Noon), new(40, true, Noon)];

        var progress = DailyChallengeRules.Evaluate(Day, exercises, answers, TimeZoneInfo.Utc);

        progress.Items.Should().HaveCount(3);
        progress.Items.Single(i => i.Exercise == "GuessNote").Answered.Should().Be(2);
    }

    [Fact]
    public void CompletedDays_ListsTheDaysWhoseChallengeWasCompleted_OldestFirst()
    {
        List<PracticeAnswer> answers =
        [
            .. Complete(Noon),
            .. Complete(Noon.AddDays(-1)).Skip(1),
            .. Complete(Noon.AddDays(-2)),
            new(99, true, Noon.AddDays(-3)),
        ];

        DailyChallengeRules.CompletedDays(Three, answers, TimeZoneInfo.Utc).Should().Equal(Day.AddDays(-2), Day);
    }

    [Fact]
    public void CompletedDays_CountsAnswersOnThePlayersCalendarDay()
    {
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        // Oct 6, 02:00 UTC is still Oct 5, 22:00 in Toronto.
        var answers = Complete(new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc));

        DailyChallengeRules.CompletedDays(Three, answers, toronto).Should().Equal(Day);
        DailyChallengeRules.CompletedDays(Three, answers, TimeZoneInfo.Utc).Should().Equal(Day.AddDays(1));
    }

    [Fact]
    public void CompletedDays_AgreesWithEvaluate()
    {
        var exercises = Seeded.Value;
        var dates = Enumerable.Range(0, 30).Select(i => Day.AddDays(i)).ToList();
        var answers = dates
            .SelectMany((date, i) =>
            {
                var day = Complete(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), DailyChallengeRules.Pick(date, exercises));
                // Every third day stops one answer short.
                return i % 3 == 0 ? day.SkipLast(1) : day;
            })
            .ToList();

        var completed = DailyChallengeRules.CompletedDays(exercises, answers, TimeZoneInfo.Utc);

        completed.Should().HaveCount(20)
            .And.Equal(dates.Where(d => DailyChallengeRules.Evaluate(d, exercises, answers, TimeZoneInfo.Utc).IsComplete));
    }

    // Each exercise answered as many times as the challenge asks, a minute apart from <from>.
    private static List<PracticeAnswer> Complete(DateTime from, IEnumerable<ChallengeExercise>? exercises = null) =>
        (exercises ?? Three)
            .SelectMany(e => Enumerable.Repeat(e.ExerciseId, DailyChallengeRules.Target(e.Name)))
            .Select((id, i) => new PracticeAnswer(id, false, from.AddMinutes(i)))
            .ToList();

    private static IReadOnlyList<ChallengeExercise> LoadSeededExercises()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"daily-challenge-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        return db.Exercises
            .Select(e => new ChallengeExercise(e.ExerciseId, e.Name, e.ExerciseCategory.Name))
            .ToList();
    }
}
