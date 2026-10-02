using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.ExerciseValidators;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Free practice shows a round's answer before it is answered
/// (<see cref="IExerciseValidator.AnswerOf"/>). The answer shown must be one the
/// validator accepts, for every seeded exercise and the rounds the planner really makes.
/// </summary>
public class FreePracticeAnswerTests
{
    private static readonly IMusicTheoryService Theory = new MusicTheoryServiceAdapter();

    private static readonly ExerciseValidatorRegistry Registry = new(
        typeof(IExerciseValidator).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IExerciseValidator).IsAssignableFrom(type))
            .Select(type => type.GetConstructor([typeof(IMusicTheoryService)]) is { } withTheory
                ? (IExerciseValidator)withTheory.Invoke([Theory])
                : (IExerciseValidator)Activator.CreateInstance(type)!));

    public static TheoryData<string> SeededExercises
    {
        get
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"free-practice-{Guid.NewGuid():N}")
                .Options);
            SeedData.SeedExercises(db);

            var data = new TheoryData<string>();
            foreach (var name in db.Exercises.Select(e => e.Name).OrderBy(n => n).ToList())
            {
                data.Add(name);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SeededExercises))]
    public void TheAnswerShown_IsAccepted(string exerciseName)
    {
        var validator = Registry.Get(exerciseName);
        validator.Should().NotBeNull("{0} needs a validator", exerciseName);
        var planner = new ExercisePlaybackPlanner();
        var exercise = new Exercise { ExerciseId = 1, Name = exerciseName };

        for (var i = 0; i < 40; i++)
        {
            // The filters RequestPlay always adds.
            var filters = new Dictionary<string, string> { ["instrument"] = "Piano", ["noteRange"] = "C4-C4" };
            var json = planner.Plan(exercise, filters).ExpectedAnswerJson;

            var answer = validator!.AnswerOf(json);

            answer.Should().NotBeNullOrWhiteSpace(json);
            validator.Validate(answer, json).Should().Be(new ExerciseValidationResult(true, answer), json);
        }
    }

    [Fact]
    public void TheAnswerShown_IsTheAnswerReported_ForAnEmptyGuess()
    {
        var validator = new RecordingValidator();

        ((IExerciseValidator)validator).AnswerOf("""{"root":"Db","quality":"minor"}""").Should().Be("Db|minor");
        validator.Guesses.Should().Equal(string.Empty);
    }

    private sealed class RecordingValidator : IExerciseValidator
    {
        private readonly GuessChordsValidator _inner = new(Theory);

        public List<string> Guesses { get; } = [];

        public string ExerciseName => _inner.ExerciseName;

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            Guesses.Add(userGuess);
            return _inner.Validate(userGuess, expectedAnswerJson);
        }
    }
}
