using System.Net.Http.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A scored answer is saved with the exercise filters its round was played with: only
/// the exercise's own filter groups, never the instrument, guitar position or note range.
/// </summary>
public class AnswerFiltersTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public AnswerFiltersTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Fact]
    public async Task Answers_KeepTheExerciseFiltersTheyWerePlayedWith()
    {
        var exerciseId = SeededExercise("GuessInterval");

        var filterJson = await AnswerAsync(exerciseId, guess: "2",
            new { keySelect = "D4", scaleTypeSelect = "minor", instrument = "Piano", bogus = "x" });

        filterJson.Should().Be("""{"keySelect":"D4","scaleTypeSelect":"minor"}""");
    }

    [Fact]
    public async Task Answers_ToExercisesWithoutFilters_KeepNone()
    {
        var exerciseId = SeededExercise("GuessNote");

        (await AnswerAsync(exerciseId, guess: "C4", new { instrument = "Piano" })).Should().BeNull();
    }

    [Fact]
    public async Task SheetMusicAnswers_KeepTheirFiltersToo()
    {
        // No sheet-music exercise has filters yet, so this one is given a group of its own.
        var exerciseId = AddExercise("SolfegeMelody", new FilterOptionGroup
        {
            Name = "voice",
            Label = "Voice",
            Options = [new("mezzo", "Mezzo"), new("tenor", "Tenor")],
        });

        var filterJson = await AnswerAsync(exerciseId, guess: "C4", new { voice = "tenor" });

        filterJson.Should().Be("""{"voice":"tenor"}""");
    }

    /// <summary>Plays a scored round with <paramref name="filters"/>, answers it and returns the saved filters.</summary>
    private async Task<string?> AnswerAsync(int exerciseId, string guess, object filters)
    {
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, filters }));
        var roundId = play.TryGetProperty("roundId", out var round) ? round.GetString() : null;
        var validation = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId, userGuess = guess, timeSpentSeconds = 3 }));
        validation.GetProperty("success").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ScoreSnapshots
            .Where(s => s.UserId == SignedInWebApplicationFactory.UserId && s.ExerciseId == exerciseId)
            .OrderByDescending(s => s.Id)
            .Select(s => s.FilterJson)
            .FirstAsync();
    }

    private int SeededExercise(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Exercises.First(e => e.Name == name).ExerciseId;
    }

    private int AddExercise(string name, params FilterOptionGroup[] filters)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exercise = new Exercise { Name = name, Description = name, FiltersJson = JsonConvert.SerializeObject(filters) };
        db.Exercises.Add(exercise);
        db.SaveChanges();
        return exercise.ExerciseId;
    }
}
