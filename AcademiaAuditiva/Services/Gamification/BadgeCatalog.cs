namespace AcademiaAuditiva.Services.Gamification;

public enum BadgeGroup
{
    Dedication,
    Mastery,
    Progress,
    Fun
}

/// <param name="Key">Primary key in the Badges table.</param>
/// <param name="Icon">Bootstrap Icons class.</param>
/// <param name="IsAvailable">False when the badge is seeded but no rule awards it yet; players never see it.</param>
public sealed record BadgeDefinition(string Key, BadgeGroup Group, string Icon, bool IsAvailable = true);

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
        new(BadgeKeys.FirstSession, BadgeGroup.Dedication, "bi-music-note-beamed"),
        new(BadgeKeys.ThreeDays, BadgeGroup.Dedication, "bi-calendar-check"),
        new(BadgeKeys.FiveDays, BadgeGroup.Dedication, "bi-calendar-week"),
        new(BadgeKeys.Marathon20Min, BadgeGroup.Dedication, "bi-stopwatch"),
        new(BadgeKeys.FaithfulPractitioner, BadgeGroup.Dedication, "bi-journal-check"),
        new(BadgeKeys.TenSessionsWeek, BadgeGroup.Dedication, "bi-lightning-charge"),

        new(BadgeKeys.MasterChords, BadgeGroup.Mastery, "bi-stack"),
        new(BadgeKeys.SharpListener, BadgeGroup.Mastery, "bi-ear"),
        new(BadgeKeys.RhythmMaestro, BadgeGroup.Mastery, "bi-soundwave"),
        new(BadgeKeys.MelodyExplorer, BadgeGroup.Mastery, "bi-music-note-list"),
        new(BadgeKeys.ScaleClimber, BadgeGroup.Mastery, "bi-bar-chart-steps"),

        new(BadgeKeys.ComebackKid, BadgeGroup.Progress, "bi-arrow-repeat"),
        new(BadgeKeys.AdvancedConqueror, BadgeGroup.Progress, "bi-trophy"),
        new(BadgeKeys.PersistentStudent, BadgeGroup.Progress, "bi-graph-up-arrow"),
        new(BadgeKeys.NotableProgress, BadgeGroup.Progress, "bi-bar-chart-line"),
        new(BadgeKeys.ResilientEar, BadgeGroup.Progress, "bi-shield-check"),
        new(BadgeKeys.IntervalTamer, BadgeGroup.Progress, "bi-arrows-expand"),

        new(BadgeKeys.BadgeCollector, BadgeGroup.Fun, "bi-gem"),

        // These need features the app doesn't have yet (filter tracking, daily
        // challenges, missions, speed tests, random mode).
        new(BadgeKeys.Explorer, BadgeGroup.Dedication, "bi-compass", IsAvailable: false),
        new(BadgeKeys.FilterNinja, BadgeGroup.Dedication, "bi-funnel", IsAvailable: false),
        new(BadgeKeys.DailyChallengeComplete, BadgeGroup.Dedication, "bi-calendar-event", IsAvailable: false),
        new(BadgeKeys.TotalMastery, BadgeGroup.Progress, "bi-star", IsAvailable: false),
        new(BadgeKeys.MissionAddict, BadgeGroup.Fun, "bi-flag", IsAvailable: false),
        new(BadgeKeys.Speedster, BadgeGroup.Fun, "bi-speedometer2", IsAvailable: false),
        new(BadgeKeys.MysteryListener, BadgeGroup.Fun, "bi-question-circle", IsAvailable: false),
        new(BadgeKeys.ImpossibleMelody, BadgeGroup.Fun, "bi-music-note", IsAvailable: false),
    ];

    public static IReadOnlyList<BadgeDefinition> Available { get; } = All.Where(b => b.IsAvailable).ToArray();

    public static IReadOnlyList<BadgeGroup> Groups { get; } =
        [BadgeGroup.Dedication, BadgeGroup.Mastery, BadgeGroup.Progress, BadgeGroup.Fun];

    private static readonly Dictionary<string, BadgeDefinition> ByKey =
        All.ToDictionary(b => b.Key, StringComparer.Ordinal);

    public static BadgeDefinition? Find(string key) => ByKey.GetValueOrDefault(key);
}
