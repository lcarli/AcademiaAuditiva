using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services.Gamification;

/// <param name="EarnedAtUtc">When the badge was awarded; null while locked.</param>
/// <param name="EarnedOn">Award date in the player's time zone.</param>
/// <param name="IsNew">Earned but not yet seen on the badges page.</param>
public sealed record BadgeView(
    string Key,
    BadgeGroup Group,
    string Title,
    string Description,
    bool IsEarned,
    DateTime? EarnedAtUtc,
    DateOnly? EarnedOn,
    bool IsNew);

/// <param name="Badges">Every available badge, earned or locked, in catalog order.</param>
public sealed record GamificationProfile(PlayerProgress Progress, IReadOnlyList<BadgeView> Badges)
{
    public int EarnedCount => Badges.Count(b => b.IsEarned);

    public int NewCount => Badges.Count(b => b.IsNew);

    public IReadOnlyList<BadgeView> LatestEarned(int count) => Badges
        .Where(b => b.IsEarned)
        .OrderByDescending(b => b.EarnedAtUtc)
        .Take(count)
        .ToList();
}

/// <param name="XpGained">XP for this answer plus any badge it unlocked.</param>
/// <param name="NewBadges">Badges awarded by this answer.</param>
public sealed record AttemptRewards(PlayerProgress Progress, int XpGained, bool LevelUp, IReadOnlyList<BadgeView> NewBadges);

public interface IGamificationService
{
    /// <summary>Call after the answer's ScoreSnapshot is saved; awards any badge it unlocked.</summary>
    Task<AttemptRewards> RecordAttemptAsync(string userId, bool isCorrect, TimeZoneInfo timeZone, CancellationToken ct = default);

    /// <summary>Progress and badges, awarding any badge already earned by past answers.</summary>
    Task<GamificationProfile> GetProfileAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default);

    Task MarkBadgesSeenAsync(string userId, CancellationToken ct = default);
}

public sealed class GamificationService : IGamificationService
{
    private readonly ApplicationDbContext _db;
    private readonly PracticeHistory _history;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<GamificationService> _logger;
    private readonly TimeProvider _clock;

    public GamificationService(
        ApplicationDbContext db,
        PracticeHistory history,
        IStringLocalizer<SharedResources> localizer,
        ILogger<GamificationService> logger,
        TimeProvider clock)
    {
        _db = db;
        _history = history;
        _localizer = localizer;
        _logger = logger;
        _clock = clock;
    }

    public async Task<AttemptRewards> RecordAttemptAsync(string userId, bool isCorrect, TimeZoneInfo timeZone, CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var state = await LoadAsync(userId, ct);
        var awarded = await AwardAsync(userId, state, timeZone, nowUtc, ct);
        var progress = BuildProgress(state, timeZone, nowUtc);

        var xpGained = (isCorrect ? Leveling.XpPerCorrectAnswer : Leveling.XpPerWrongAnswer)
            + awarded.Count * Leveling.XpPerBadge;
        var levelBefore = Leveling.LevelForXp(Math.Max(0, progress.Xp - xpGained));
        var newBadges = awarded
            .Select(key => ToView(BadgeCatalog.Find(key)!, state.Earned[key], timeZone))
            .ToList();

        return new AttemptRewards(progress, xpGained, progress.Level > levelBefore, newBadges);
    }

    public async Task<GamificationProfile> GetProfileAsync(string userId, TimeZoneInfo timeZone, CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var state = await LoadAsync(userId, ct);
        await AwardAsync(userId, state, timeZone, nowUtc, ct);

        var badges = BadgeCatalog.Available
            .Select(def => ToView(def, state.Earned.GetValueOrDefault(def.Key), timeZone))
            .ToList();
        return new GamificationProfile(BuildProgress(state, timeZone, nowUtc), badges);
    }

    public async Task MarkBadgesSeenAsync(string userId, CancellationToken ct = default)
    {
        var unseen = await _db.BadgesEarned
            .Where(b => b.UserId == userId && b.IsNew)
            .ToListAsync(ct);
        if (unseen.Count == 0) return;

        foreach (var badge in unseen) badge.IsNew = false;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<PlayerState> LoadAsync(string userId, CancellationToken ct)
    {
        var answers = await _history.GetAsync(userId, ct);

        var exercises = await _db.Exercises.AsNoTracking()
            .Select(e => new ExerciseInfo(
                e.ExerciseId,
                e.ExerciseType != null ? e.ExerciseType.Name : "",
                e.ExerciseCategory != null ? e.ExerciseCategory.Name : "",
                e.DifficultyLevel != null ? e.DifficultyLevel.Name : ""))
            .ToDictionaryAsync(e => e.ExerciseId, ct);

        var state = new PlayerState(answers, exercises);
        await LoadEarnedAsync(userId, state, ct);
        return state;
    }

    private async Task LoadEarnedAsync(string userId, PlayerState state, CancellationToken ct)
    {
        var rows = await _db.BadgesEarned.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => new { b.BadgeKey, b.EarnedDate, b.IsNew })
            .ToListAsync(ct);

        state.Earned.Clear();
        foreach (var row in rows)
        {
            state.Earned.TryAdd(row.BadgeKey, new EarnedBadge(AsUtc(row.EarnedDate), row.IsNew));
        }
    }

    /// <summary>Saves the badges the rules award now and returns their keys.</summary>
    private async Task<IReadOnlyList<string>> AwardAsync(
        string userId, PlayerState state, TimeZoneInfo timeZone, DateTime nowUtc, CancellationToken ct)
    {
        var candidates = BadgeRules.Evaluate(
            state.Answers, state.Exercises, state.Earned.Keys.ToHashSet(StringComparer.Ordinal), timeZone, nowUtc);
        if (candidates.Count == 0) return [];

        // BadgesEarned has a foreign key to Badges, which SeedData keeps in sync with the catalog.
        var seeded = await _db.Badges.AsNoTracking().Select(b => b.BadgeKey).ToListAsync(ct);
        var pending = candidates.Where(key => seeded.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();

        for (var attempt = 0; attempt < 2 && pending.Count > 0; attempt++)
        {
            var rows = pending
                .Select(key => new BadgesEarned { UserId = userId, BadgeKey = key, EarnedDate = nowUtc, IsNew = true })
                .ToList();
            _db.BadgesEarned.AddRange(rows);
            try
            {
                await _db.SaveChangesAsync(ct);
                foreach (var key in pending) state.Earned[key] = new EarnedBadge(nowUtc, true);
                return pending;
            }
            catch (DbUpdateException ex)
            {
                // Usually a parallel answer (second tab, double submit) awarded
                // the same badge first: the unique (UserId, BadgeKey) index wins.
                foreach (var row in rows) _db.Entry(row).State = EntityState.Detached;
                _logger.LogInformation(ex, "Badge award conflicted; reloading earned badges (attempt {Attempt}).", attempt + 1);
                await LoadEarnedAsync(userId, state, ct);
                pending = pending.Where(key => !state.Earned.ContainsKey(key)).ToList();
            }
        }

        if (pending.Count > 0)
        {
            _logger.LogWarning("Could not award {Count} badge(s): {Badges}.", pending.Count, string.Join(", ", pending));
        }
        return [];
    }

    private static PlayerProgress BuildProgress(PlayerState state, TimeZoneInfo timeZone, DateTime nowUtc)
    {
        var correct = state.Answers.Count(a => a.IsCorrect);
        var badges = state.Earned.Keys.Count(key => BadgeCatalog.Find(key)?.IsAvailable == true);
        var streak = PracticeStreak.Compute(state.Answers.Select(a => a.Timestamp), timeZone, nowUtc);
        return PlayerProgress.From(correct, state.Answers.Count, badges, streak);
    }

    private BadgeView ToView(BadgeDefinition badge, EarnedBadge? earned, TimeZoneInfo timeZone) => new(
        badge.Key,
        badge.Group,
        _localizer[$"Badge.{badge.Key}.Title"].Value,
        _localizer[$"Badge.{badge.Key}.Description"].Value,
        IsEarned: earned is not null,
        EarnedAtUtc: earned?.EarnedAtUtc,
        EarnedOn: earned is null ? null : PracticeStreak.LocalDate(earned.EarnedAtUtc, timeZone),
        IsNew: earned?.IsNew ?? false);

    // SQL Server datetime2 comes back as DateTimeKind.Unspecified; the app always stores UTC.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record EarnedBadge(DateTime EarnedAtUtc, bool IsNew);

    private sealed class PlayerState(IReadOnlyList<PracticeAnswer> answers, Dictionary<int, ExerciseInfo> exercises)
    {
        public IReadOnlyList<PracticeAnswer> Answers { get; } = answers;

        public Dictionary<int, ExerciseInfo> Exercises { get; } = exercises;

        public Dictionary<string, EarnedBadge> Earned { get; } = new(StringComparer.Ordinal);
    }
}
