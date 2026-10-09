using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The catalog lists the exercises the seed writes to the database, without asking it.
/// </summary>
public class ExerciseCatalogTests
{
    [Fact]
    public void TheCatalog_IsTheSeededExercises_InTheirCategories()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"exercise-catalog-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);

        var seeded = db.Exercises
            .Join(db.ExerciseCategories, e => e.ExerciseCategoryId, c => c.Id,
                (e, c) => new { e.Name, Category = c.Name, e.DifficultyLevelId })
            .AsEnumerable()
            .Select(e => new CatalogExercise(e.Name, e.Category, e.DifficultyLevelId, TrainingTracks.OfCategory(e.Category)!))
            .ToList();

        seeded.Should().OnlyContain(e => e.Track != null, "every seeded category belongs to a track");
        ExerciseCatalog.All.Should().BeEquivalentTo(seeded);
        ExerciseCatalog.Count.Should().Be(seeded.Count).And.BeGreaterThan(29);
        ExerciseCatalog.All.Select(e => e.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ByCategory_ListsEveryExerciseOnce_TrackByTrack_TheEasiestFirst()
    {
        ExerciseCatalog.ByCategory.SelectMany(c => c.Exercises).Should().BeEquivalentTo(ExerciseCatalog.All);
        ExerciseCatalog.ByCategory.Select(c => c.Name).Should().Equal(
            "EarTraining", "Melody", "Harmony", "Scales", "Rhythm", "Level", "StereoPhase");
        ExerciseCatalog.ByCategory.Should().AllSatisfy(c =>
        {
            c.Exercises.Should().NotBeEmpty().And.OnlyContain(e => e.Category == c.Name);
            c.Exercises.Select(e => e.Difficulty).Should().BeInAscendingOrder();
        });
    }

    // Within a difficulty, an exercise keeps its place in the seed, so a new one shows up
    // next to the ones it was seeded with.
    [Fact]
    public void ByCategory_KeepsTheSeedsOrder_WithinADifficulty()
    {
        var order = ExerciseCatalog.All.Select(e => e.Name).ToList();

        foreach (var category in ExerciseCatalog.ByCategory)
        {
            foreach (var level in category.Exercises.GroupBy(e => e.Difficulty))
                level.Select(e => order.IndexOf(e.Name)).Should().BeInAscendingOrder();
        }

        ExerciseCatalog.ByCategory[0].Exercises.Select(e => e.Name).Should().ContainInOrder("HigherOrLower", "GuessTuning");
    }
}
