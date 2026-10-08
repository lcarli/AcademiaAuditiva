namespace AcademiaAuditiva.Services;

/// <summary>
/// The training tracks (docs/Audio-Ear-Training.md): Music Ear Training, Audio Ear Training and
/// Mix Challenges. Each exercise category belongs to exactly one track, which lists it here in the
/// order its pages show the categories, and an exercise belongs to its category's track.
/// <para>
/// Until each track's place in them is decided, the learning path, the daily challenge, the game
/// modes and the badges that ask for every exercise or category take Music exercises only
/// (<see cref="IsMusic"/>), so exercises of the other tracks don't change them.
/// </para>
/// </summary>
public static class TrainingTracks
{
    public const string Music = "Music";
    public const string Audio = "Audio";
    public const string Mix = "Mix";

    /// <summary>Every track, in the order the pages show them.</summary>
    public static IReadOnlyList<TrainingTrack> All { get; } =
    [
        new(Music, ["EarTraining", "Melody", "Harmony", "Scales", "Rhythm", "Games", "Misc"]),
        new(Audio, ["Level", "FrequencyEq", "Dynamics", "StereoPhase", "SpaceTime", "CriticalListening"]),
        new(Mix, []),
    ];

    private static readonly Dictionary<string, string> TrackByCategory = All
        .SelectMany(t => t.Categories, (t, category) => (category, t.Key))
        .ToDictionary(x => x.category, x => x.Key, StringComparer.Ordinal);

    /// <summary>The track whose key is <paramref name="key"/>, ignoring case, or null.</summary>
    public static TrainingTrack? Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The key of the track that lists <paramref name="category"/>, or null when none does.</summary>
    public static string? OfCategory(string category) => TrackByCategory.GetValueOrDefault(category);

    /// <summary>
    /// Whether the exercises of <paramref name="category"/> take part in what only Music exercises
    /// take part in so far. A category no track lists counts as Music: the seeded ones are all
    /// listed (ExerciseCatalog fails on any other).
    /// </summary>
    public static bool IsMusic(string category) => OfCategory(category) is null or Music;
}

/// <param name="Key">Its key, used in <c>/Exercise?track=</c> and in its texts (<c>TrainingTrack.{Key}</c>).</param>
/// <param name="Categories">The names of its exercise categories, in the order its pages show them.</param>
public sealed record TrainingTrack(string Key, IReadOnlyList<string> Categories);
