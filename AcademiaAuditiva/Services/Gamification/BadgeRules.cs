namespace AcademiaAuditiva.Services.Gamification;

/// <summary>
/// Decides which badges a player has earned from their whole answer history.
/// Pure and deterministic, so badges can be awarded retroactively and the
/// rules are unit-tested without a database.
/// </summary>
public static class BadgeRules
{
    public const int BadgeCollectorThreshold = 15;

    private static readonly TimeSpan MarathonGap = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MarathonLength = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    /// <param name="answers">All of the player's answers, oldest first.</param>
    /// <param name="exercises">Taxonomy of every exercise, by id.</param>
    /// <param name="timeZone">Player's time zone; streaks count local calendar days.</param>
    /// <returns>Badges earned now that are not in <paramref name="alreadyEarned"/>, in catalog order.</returns>
    public static IReadOnlyList<string> Evaluate(
        IReadOnlyList<PracticeAnswer> answers,
        IReadOnlyDictionary<int, ExerciseInfo> exercises,
        IReadOnlySet<string> alreadyEarned,
        TimeZoneInfo timeZone,
        DateTime nowUtc)
    {
        var awarded = new List<string>();
        if (answers.Count == 0) return awarded;

        var history = new History(answers, exercises, timeZone, nowUtc);
        var earned = new HashSet<string>(alreadyEarned, StringComparer.Ordinal);
        foreach (var badge in BadgeCatalog.Available)
        {
            if (badge.Key == BadgeKeys.BadgeCollector || earned.Contains(badge.Key)) continue;
            if (IsEarned(badge.Key, history))
            {
                awarded.Add(badge.Key);
                earned.Add(badge.Key);
            }
        }

        var collected = BadgeCatalog.Available.Count(b => b.Key != BadgeKeys.BadgeCollector && earned.Contains(b.Key));
        if (!earned.Contains(BadgeKeys.BadgeCollector) && collected >= BadgeCollectorThreshold)
        {
            awarded.Add(BadgeKeys.BadgeCollector);
        }
        return awarded;
    }

    private static bool IsEarned(string key, History h) => key switch
    {
        BadgeKeys.FirstSession => h.Answers.Count > 0,
        BadgeKeys.ThreeDays => h.BestStreak >= 3,
        BadgeKeys.FiveDays => h.BestStreak >= 5,
        BadgeKeys.Marathon20Min => PracticeSessions.Split(h.Answers, MarathonGap)
            .Any(run => run[^1].Timestamp - run[0].Timestamp >= MarathonLength),
        BadgeKeys.FaithfulPractitioner => h.Sessions.Count >= 30,
        BadgeKeys.TenSessionsWeek => HasSessionsWithin(h.Sessions, 10, Week),

        BadgeKeys.MasterChords => h.Exercises(e => e.Type == "ChordRecognition")
            .Count(id => PracticeSessions.HasWindow(h.Results(id), 20, 18)) >= 3,
        BadgeKeys.SharpListener => h.Exercises(e => e.Category == "EarTraining")
            .Count(id => PracticeSessions.HasWindow(h.Results(id), 20, 18)) >= 3,
        BadgeKeys.RhythmMaestro => h.Exercises(e => e.Category == "Rhythm")
            .Sum(id => h.SessionsOf(id).Count(s => s.All(a => a.IsCorrect))) >= 2,
        BadgeKeys.MelodyExplorer => AllOf(h.Exercises(e => e.Category == "Melody"),
            id => PracticeSessions.HasWindow(h.Results(id), 10, 8)),
        BadgeKeys.ScaleClimber => AllOf(h.Exercises(e => e.Type == "ScaleRecognition"),
            id => h.Results(id).Count(correct => correct) >= 5),

        BadgeKeys.ComebackKid => h.PracticedExercises.Any(id => IsComeback(h.Results(id))),
        BadgeKeys.AdvancedConqueror => h.Exercises(e => e.Difficulty == "Advanced")
            .Count(id => PracticeSessions.HasWindow(h.Results(id), 10, 7)) >= 5,
        BadgeKeys.PersistentStudent => h.PracticedExercises.Any(id => HasRisingRun(h.SessionsOf(id), 4)),
        BadgeKeys.NotableProgress => HasNotableProgress(h),
        BadgeKeys.ResilientEar => h.PracticedExercises.Any(id => BouncedBack(h.Results(id), 3)),
        BadgeKeys.IntervalTamer => h.Exercises(e => e.Type == "IntervalRecognition")
            .Sum(id => h.SessionsOf(id).Count(s => PracticeSessions.AccuracyAtLeast(s, 80))) >= 10,

        _ => false
    };

    private static bool AllOf(IEnumerable<int> exerciseIds, Func<int, bool> predicate)
    {
        var ids = exerciseIds.ToList();
        return ids.Count > 0 && ids.All(predicate);
    }

    private static bool HasSessionsWithin(List<List<PracticeAnswer>> sessions, int count, TimeSpan span)
    {
        for (var i = 0; i + count - 1 < sessions.Count; i++)
        {
            if (sessions[i + count - 1][0].Timestamp - sessions[i][0].Timestamp < span) return true;
        }
        return false;
    }

    // At most 2 of the first 5 answers right, then 8 of 10 in a row later on.
    private static bool IsComeback(IReadOnlyList<bool> results) =>
        results.Count >= 15
        && results.Take(5).Count(correct => correct) <= 2
        && PracticeSessions.HasWindow(results.Skip(5).ToList(), 10, 8);

    private static bool HasRisingRun(List<List<PracticeAnswer>> sessions, int length)
    {
        var run = 1;
        for (var i = 1; i < sessions.Count; i++)
        {
            run = IsMoreAccurate(sessions[i], sessions[i - 1]) ? run + 1 : 1;
            if (run >= length) return true;
        }
        return false;
    }

    private static bool IsMoreAccurate(List<PracticeAnswer> later, List<PracticeAnswer> earlier) =>
        later.Count(a => a.IsCorrect) * earlier.Count > earlier.Count(a => a.IsCorrect) * later.Count;

    private static bool BouncedBack(IReadOnlyList<bool> results, int misses)
    {
        var wrongInARow = 0;
        foreach (var correct in results)
        {
            if (!correct)
            {
                wrongInARow++;
                continue;
            }
            if (wrongInARow >= misses) return true;
            wrongInARow = 0;
        }
        return false;
    }

    // Last 30 days against the 30 days before, per category. Only categories
    // with at least 10 answers in both periods are compared; at least two of
    // them must exist and every one must have improved.
    private static bool HasNotableProgress(History h)
    {
        var recentStart = h.NowUtc - Month;
        var previousStart = recentStart - Month;
        var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var answer in h.Answers)
        {
            if (answer.Timestamp <= previousStart || answer.Timestamp > h.NowUtc) continue;
            if (!h.TryGetExercise(answer.ExerciseId, out var info) || info.Category.Length == 0) continue;

            if (!tallies.TryGetValue(info.Category, out var tally))
            {
                tally = new Tally();
                tallies[info.Category] = tally;
            }

            if (answer.Timestamp > recentStart)
            {
                tally.RecentTotal++;
                if (answer.IsCorrect) tally.RecentCorrect++;
            }
            else
            {
                tally.PreviousTotal++;
                if (answer.IsCorrect) tally.PreviousCorrect++;
            }
        }

        var compared = tallies.Values.Where(t => t.RecentTotal >= 10 && t.PreviousTotal >= 10).ToList();
        return compared.Count >= 2
            && compared.All(t => t.RecentCorrect * t.PreviousTotal > t.PreviousCorrect * t.RecentTotal);
    }

    private sealed class Tally
    {
        public int RecentCorrect;
        public int RecentTotal;
        public int PreviousCorrect;
        public int PreviousTotal;
    }

    private sealed class History
    {
        private readonly IReadOnlyDictionary<int, ExerciseInfo> _exercises;
        private readonly Dictionary<int, List<PracticeAnswer>> _byExercise;
        private readonly Dictionary<int, List<bool>> _results = [];
        private readonly Dictionary<int, List<List<PracticeAnswer>>> _sessionsByExercise = [];
        private List<List<PracticeAnswer>>? _sessions;
        private int? _bestStreak;

        public History(
            IReadOnlyList<PracticeAnswer> answers,
            IReadOnlyDictionary<int, ExerciseInfo> exercises,
            TimeZoneInfo timeZone,
            DateTime nowUtc)
        {
            Answers = answers;
            TimeZone = timeZone;
            NowUtc = nowUtc;
            _exercises = exercises;
            _byExercise = answers.GroupBy(a => a.ExerciseId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public IReadOnlyList<PracticeAnswer> Answers { get; }

        public TimeZoneInfo TimeZone { get; }

        public DateTime NowUtc { get; }

        public IEnumerable<int> PracticedExercises => _byExercise.Keys;

        /// <summary>Counted sessions across all exercises.</summary>
        public List<List<PracticeAnswer>> Sessions => _sessions ??= PracticeSessions.Counted(Answers);

        public int BestStreak => _bestStreak ??=
            PracticeStreak.Compute(Answers.Select(a => a.Timestamp), TimeZone, NowUtc).Best;

        public bool TryGetExercise(int exerciseId, out ExerciseInfo info) =>
            _exercises.TryGetValue(exerciseId, out info!);

        /// <summary>Every known exercise matching the predicate, practiced or not.</summary>
        public IEnumerable<int> Exercises(Func<ExerciseInfo, bool> predicate) =>
            _exercises.Values.Where(predicate).Select(e => e.ExerciseId);

        /// <summary>Right/wrong results of one exercise, oldest first.</summary>
        public IReadOnlyList<bool> Results(int exerciseId)
        {
            if (!_results.TryGetValue(exerciseId, out var results))
            {
                results = _byExercise.TryGetValue(exerciseId, out var answers)
                    ? answers.Select(a => a.IsCorrect).ToList()
                    : [];
                _results[exerciseId] = results;
            }
            return results;
        }

        /// <summary>Counted sessions of one exercise (pauses in other exercises don't split them).</summary>
        public List<List<PracticeAnswer>> SessionsOf(int exerciseId)
        {
            if (!_sessionsByExercise.TryGetValue(exerciseId, out var sessions))
            {
                sessions = _byExercise.TryGetValue(exerciseId, out var answers)
                    ? PracticeSessions.Counted(answers)
                    : [];
                _sessionsByExercise[exerciseId] = sessions;
            }
            return sessions;
        }
    }
}
