namespace AcademiaAuditiva.Services.Gamification;

public enum BadgeGroup
{
    Dedication,
    Mastery,
    Progress,
    Fun
}

/// <param name="Key">Primary key in the Badges table, and the name of the medal art (see <see cref="BadgeDisplay"/>).</param>
/// <param name="IsAvailable">False when the badge is seeded but no rule awards it yet; players never see it.</param>
public sealed record BadgeDefinition(string Key, BadgeGroup Group, bool IsAvailable = true);

public static class BadgeKeys
{
    public const string FirstSession = "first_session";
    public const string ThreeDays = "3_days";
    public const string FiveDays = "5_days";
    public const string Marathon20Min = "marathon_20min";
    public const string FaithfulPractitioner = "faithful_practitioner";
    public const string TenSessionsWeek = "10_sessions_week";
    public const string Explorer = "explorer";
    public const string FilterNinja = "filter_ninja";
    public const string DailyChallengeComplete = "daily_challenge_complete";

    public const string MasterChords = "master_chords";
    public const string SharpListener = "sharp_listener";
    public const string RhythmMaestro = "rhythm_maestro";
    public const string MelodyExplorer = "melody_explorer";
    public const string ScaleClimber = "scale_climber";

    public const string ComebackKid = "comeback_kid";
    public const string AdvancedConqueror = "advanced_conqueror";
    public const string PersistentStudent = "persistent_student";
    public const string TotalMastery = "total_mastery";
    public const string NotableProgress = "notable_progress";
    public const string ResilientEar = "resilient_ear";
    public const string IntervalTamer = "interval_tamer";

    public const string MissionAddict = "mission_addict";
    public const string Speedster = "speedster";
    public const string MysteryListener = "mystery_listener";
    public const string ImpossibleMelody = "impossible_melody";
    public const string BadgeCollector = "badge_collector";
}

/// <summary>
/// Every badge seeded in the Badges table, in display order. Titles and
/// descriptions come from the resource files (Badge.{key}.Title/Description);
/// the Portuguese texts stored in the table are not shown.
/// </summary>
public static class BadgeCatalog
{
    public static IReadOnlyList<BadgeDefinition> All { get; } =
    [
        new(BadgeKeys.FirstSession, BadgeGroup.Dedication),
        new(BadgeKeys.ThreeDays, BadgeGroup.Dedication),
        new(BadgeKeys.FiveDays, BadgeGroup.Dedication),
        new(BadgeKeys.Marathon20Min, BadgeGroup.Dedication),
        new(BadgeKeys.FaithfulPractitioner, BadgeGroup.Dedication),
        new(BadgeKeys.TenSessionsWeek, BadgeGroup.Dedication),

        new(BadgeKeys.MasterChords, BadgeGroup.Mastery),
        new(BadgeKeys.SharpListener, BadgeGroup.Mastery),
        new(BadgeKeys.RhythmMaestro, BadgeGroup.Mastery),
        new(BadgeKeys.MelodyExplorer, BadgeGroup.Mastery),
        new(BadgeKeys.ScaleClimber, BadgeGroup.Mastery),

        new(BadgeKeys.ComebackKid, BadgeGroup.Progress),
        new(BadgeKeys.AdvancedConqueror, BadgeGroup.Progress),
        new(BadgeKeys.PersistentStudent, BadgeGroup.Progress),
        new(BadgeKeys.NotableProgress, BadgeGroup.Progress),
        new(BadgeKeys.ResilientEar, BadgeGroup.Progress),
        new(BadgeKeys.IntervalTamer, BadgeGroup.Progress),

        new(BadgeKeys.BadgeCollector, BadgeGroup.Fun),

        // These need features the app doesn't have yet (filter tracking, daily
        // challenges, missions, speed tests, random mode).
        new(BadgeKeys.Explorer, BadgeGroup.Dedication, IsAvailable: false),
        new(BadgeKeys.FilterNinja, BadgeGroup.Dedication, IsAvailable: false),
        new(BadgeKeys.DailyChallengeComplete, BadgeGroup.Dedication, IsAvailable: false),
        new(BadgeKeys.TotalMastery, BadgeGroup.Progress, IsAvailable: false),
        new(BadgeKeys.MissionAddict, BadgeGroup.Fun, IsAvailable: false),
        new(BadgeKeys.Speedster, BadgeGroup.Fun, IsAvailable: false),
        new(BadgeKeys.MysteryListener, BadgeGroup.Fun, IsAvailable: false),
        new(BadgeKeys.ImpossibleMelody, BadgeGroup.Fun, IsAvailable: false),
    ];

    public static IReadOnlyList<BadgeDefinition> Available { get; } = All.Where(b => b.IsAvailable).ToArray();

    public static IReadOnlyList<BadgeGroup> Groups { get; } =
        [BadgeGroup.Dedication, BadgeGroup.Mastery, BadgeGroup.Progress, BadgeGroup.Fun];

    private static readonly Dictionary<string, BadgeDefinition> ByKey =
        All.ToDictionary(b => b.Key, StringComparer.Ordinal);

    public static BadgeDefinition? Find(string key) => ByKey.GetValueOrDefault(key);
}
