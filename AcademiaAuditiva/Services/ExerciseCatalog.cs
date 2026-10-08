namespace AcademiaAuditiva.Services;

/// <summary>
/// The exercises the app offers, read once from <see cref="SeedData.Exercises"/>: pages such
/// as the landing page list and count them without asking the database, and a new exercise
/// shows up there as soon as it is seeded.
/// </summary>
public static class ExerciseCatalog
{
    // The order the landing page shows the categories in: from single notes to rhythm.
    private static readonly string[] CategoryOrder = ["EarTraining", "Melody", "Harmony", "Scales", "Rhythm", "Games", "Misc"];

    /// <summary>Every exercise, in the order <see cref="SeedData.Exercises"/> lists them.</summary>
    public static IReadOnlyList<CatalogExercise> All { get; } = Load();

    public static int Count => All.Count;

    /// <summary>
    /// The exercises of each category that has any, the categories in the landing page's
    /// order and the easiest exercises first in each.
    /// </summary>
    public static IReadOnlyList<CatalogCategory> ByCategory { get; } =
    [
        .. All.GroupBy(e => e.Category)
            .OrderBy(g => Array.IndexOf(CategoryOrder, g.Key))
            .Select(g => new CatalogCategory(g.Key, [.. g.OrderBy(e => e.Difficulty)]))
    ];

    private static List<CatalogExercise> Load()
    {
        var categories = SeedData.ExerciseCategories();
        return
        [
            .. SeedData.Exercises().Select(e =>
                new CatalogExercise(e.Name, categories[e.ExerciseCategoryId - 1].Name, e.DifficultyLevelId))
        ];
    }
}

/// <param name="Name">The exercise's name, which is also its page's action and its resource key.</param>
/// <param name="Category">Its category's name (<c>ExerciseCategory.{Category}</c> in the resources).</param>
/// <param name="Difficulty">1 for beginners, 2 for intermediate, 3 for advanced.</param>
public sealed record CatalogExercise(string Name, string Category, int Difficulty);

public sealed record CatalogCategory(string Name, IReadOnlyList<CatalogExercise> Exercises);
