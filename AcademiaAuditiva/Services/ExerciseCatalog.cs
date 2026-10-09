namespace AcademiaAuditiva.Services;

/// <summary>
/// The exercises the app offers, read once from <see cref="SeedData.Exercises"/>: pages such
/// as the landing page list and count them without asking the database, and a new exercise
/// shows up there as soon as it is seeded.
/// </summary>
public static class ExerciseCatalog
{
    // The order the pages show the categories in: track by track (TrainingTracks.All), and in
    // Music, from single notes to rhythm.
    private static readonly string[] CategoryOrder = [.. TrainingTracks.All.SelectMany(t => t.Categories)];

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

    /// <summary>
    /// Every track, in <see cref="TrainingTracks.All"/>'s order, with its categories that have
    /// exercises, as <see cref="ByCategory"/> lists them. A track without exercises has none.
    /// </summary>
    public static IReadOnlyList<CatalogTrack> ByTrack { get; } =
    [
        .. TrainingTracks.All.Select(t => new CatalogTrack(t.Key, [.. ByCategory.Where(c => t.Categories.Contains(c.Name))]))
    ];

    /// <summary>The key of the track of the exercise named <paramref name="name"/>, or null when there's no such exercise.</summary>
    public static string? TrackOf(string name) => All.FirstOrDefault(e => e.Name == name)?.Track;

    private static List<CatalogExercise> Load()
    {
        var categories = SeedData.ExerciseCategories();
        return
        [
            .. SeedData.Exercises().Select(e =>
            {
                var category = categories[e.ExerciseCategoryId - 1].Name;
                var track = TrainingTracks.OfCategory(category)
                    ?? throw new InvalidOperationException($"No training track lists the category '{category}' of {e.Name}.");
                return new CatalogExercise(e.Name, category, e.DifficultyLevelId, track);
            })
        ];
    }
}

/// <param name="Name">The exercise's name, which is also its page's action and its resource key.</param>
/// <param name="Category">Its category's name (<c>ExerciseCategory.{Category}</c> in the resources).</param>
/// <param name="Difficulty">1 for beginners, 2 for intermediate, 3 for advanced.</param>
/// <param name="Track">The key of its category's track (<see cref="TrainingTracks"/>).</param>
public sealed record CatalogExercise(string Name, string Category, int Difficulty, string Track);

public sealed record CatalogCategory(string Name, IReadOnlyList<CatalogExercise> Exercises);

/// <param name="Key">The track's key (<see cref="TrainingTracks"/>).</param>
public sealed record CatalogTrack(string Key, IReadOnlyList<CatalogCategory> Categories)
{
    public IEnumerable<CatalogExercise> Exercises => Categories.SelectMany(c => c.Exercises);
}
