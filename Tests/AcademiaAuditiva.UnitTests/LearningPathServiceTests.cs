using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.LearningPath;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The catalog matches the seeded exercises; the service evaluates it from ScoreSnapshots.</summary>
public class LearningPathServiceTests
{
    private const string UserId = "player";
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Catalog_HasEverySeededExerciseButSolfegeMelody_OnceEach()
    {
        using var db = SeededDatabase();

        LearningPathCatalog.Steps.Select(s => s.Exercise).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(db.Exercises.Select(e => e.Name).Where(n => n != "SolfegeMelody"),
                "SolfegeMelody needs a microphone");
        LearningPathCatalog.Units.Select(u => u.Key).Should().OnlyHaveUniqueItems();
        LearningPathCatalog.Units.Should().OnlyContain(u => u.Steps.Count > 0);
    }

    [Fact]
    public void Catalog_GoalsAreReachable_AndPresetsUseTheExercisesOwnFilters()
    {
        using var db = SeededDatabase();
        var filters = db.Exercises.ToDictionary(e => e.Name, e => e.FiltersJson);

        foreach (var step in LearningPathCatalog.Steps)
        {
            step.Required.Should().BeInRange(1, step.Window, "{0} needs a reachable goal", step.Exercise);
            var groups = ExerciseFilterPresets.Groups(filters[step.Exercise]);
            ExerciseFilterPresets.Sanitize(step.Filters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)), groups)
                .Should().BeEquivalentTo(step.Filters, "the {0} preset only uses its own filter options", step.Exercise);
        }
    }

    [Fact]
    public async Task NewPlayer_StartsAtTheFirstStep()
    {
        await using var db = SeededDatabase();

        var progress = await Service(db).GetProgressAsync(UserId);

        progress.Units.Select(u => u.Key).Should().Equal("FirstSteps", "BuildingBlocks", "Musicianship");
        progress.Units.Select(u => u.Number).Should().Equal(1, 2, 3);
        progress.Steps.Select(s => s.Number).Should().Equal(Enumerable.Range(1, LearningPathCatalog.Steps.Count));
        progress.TotalSteps.Should().Be(LearningPathCatalog.Steps.Count);
        progress.Current!.Exercise.Should().Be("HigherOrLower");
        progress.Steps.Skip(1).Should().OnlyContain(s => s.State == StepState.Locked);
        progress.Should().BeEquivalentTo(new { CompletedSteps = 0, Percent = 0, HasStarted = false, IsComplete = false });
        progress.JustCompleted.Should().BeNull();

        var interval = progress.Steps.Single(s => s.Exercise == "GuessInterval");
        interval.ExerciseId.Should().Be(db.Exercises.Single(e => e.Name == "GuessInterval").ExerciseId);
        interval.Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["keySelect"] = "C4", ["scaleTypeSelect"] = "major" });
        interval.AppliedFilters.Select(f => $"{f.Group.Name}={f.Option.Value}")
            .Should().BeEquivalentTo("keySelect=C4", "scaleTypeSelect=major");
    }

    [Fact]
    public async Task OnlyThePlayersAnswersCount_InTheOrderTheyWereGiven()
    {
        await using var db = SeededDatabase();
        var answers = new Answers(db);
        answers.Add("HigherOrLower", correct: true, count: 8);
        answers.Add("GuessInterval", correct: true, count: 10, userId: "someone-else");
        answers.Add("GuessInterval", correct: true);
        answers.Add("GuessInterval", correct: false);
        await db.SaveChangesAsync();

        var progress = await Service(db).GetProgressAsync(UserId);

        progress.CompletedSteps.Should().Be(1);
        progress.Current.Should().BeEquivalentTo(new
        {
            Number = 2, Exercise = "GuessInterval", State = StepState.Current,
            Correct = 1, Answered = 2, Required = 7, Window = 10, Percent = 14,
        });
        progress.HasStarted.Should().BeTrue();
        progress.JustCompleted.Should().BeNull("the latest answer did not complete a step");
        progress.UnitOf(progress.Current!).Key.Should().Be("FirstSteps");
        progress.Units.Select(u => u.State).Should().Equal(StepState.Current, StepState.Locked, StepState.Locked);
    }

    [Fact]
    public async Task TheAnswerThatCompletesAUnit_IsReportedAsJustCompleted()
    {
        await using var db = SeededDatabase();
        var answers = new Answers(db);
        answers.CompleteSteps(3);
        answers.Add("GuessMissingNote", correct: true, count: 7);
        await db.SaveChangesAsync();

        var progress = await Service(db).GetProgressAsync(UserId);

        progress.JustCompleted!.Number.Should().Be(4);
        progress.UnitOf(progress.JustCompleted).State.Should().Be(StepState.Completed);
        progress.Current!.Should().BeEquivalentTo(new { Number = 5, Exercise = "IntervalMelodico" });
        progress.UnitOf(progress.Current).Key.Should().Be("BuildingBlocks");
        progress.Should().BeEquivalentTo(new { CompletedSteps = 4, Percent = 22 });
    }

    [Fact]
    public async Task CompletingEveryStep_CompletesThePath()
    {
        await using var db = SeededDatabase();
        new Answers(db).CompleteSteps(LearningPathCatalog.Steps.Count);
        await db.SaveChangesAsync();

        var progress = await Service(db).GetProgressAsync(UserId);

        progress.Should().BeEquivalentTo(new { IsComplete = true, Percent = 100, HasStarted = true });
        progress.Current.Should().BeNull();
        progress.JustCompleted!.Exercise.Should().Be("GuessNote");
        progress.Units.Should().OnlyContain(u => u.State == StepState.Completed);
    }

    [Fact]
    public async Task StepsWhoseExerciseIsNotSeeded_AreLeftOut()
    {
        await using var db = SeededDatabase();
        db.Exercises.Remove(db.Exercises.Single(e => e.Name == "GuessChords"));
        await db.SaveChangesAsync();

        var progress = await Service(db).GetProgressAsync(UserId);

        progress.TotalSteps.Should().Be(LearningPathCatalog.Steps.Count - 1);
        progress.Steps.Select(s => s.Exercise).Should().NotContain("GuessChords");
        progress.Steps.Select(s => s.Number).Should().Equal(Enumerable.Range(1, progress.TotalSteps));
    }

    private static LearningPathService Service(ApplicationDbContext db) => new(db, new PracticeHistory(db));

    private static ApplicationDbContext SeededDatabase()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"learning-path-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        return db;
    }

    /// <summary>Adds answers one minute apart, oldest first.</summary>
    private sealed class Answers
    {
        private readonly ApplicationDbContext _db;
        private readonly Dictionary<string, int> _ids;
        private DateTime _clock = Start;

        public Answers(ApplicationDbContext db)
        {
            _db = db;
            _ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);
        }

        public void Add(string exercise, bool correct, int count = 1, string userId = UserId)
        {
            for (var i = 0; i < count; i++)
            {
                _clock = _clock.AddMinutes(1);
                _db.ScoreSnapshots.Add(new ScoreSnapshot { UserId = userId, ExerciseId = _ids[exercise], IsCorrect = correct, Timestamp = _clock });
            }
        }

        public void CompleteSteps(int count)
        {
            foreach (var step in LearningPathCatalog.Steps.Take(count))
            {
                Add(step.Exercise, correct: true, count: step.Required);
            }
        }
    }
}
