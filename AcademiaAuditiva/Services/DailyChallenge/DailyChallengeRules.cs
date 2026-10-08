using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.Services.DailyChallenge;

/// <summary>An exercise the challenge can draw.</summary>
/// <param name="Category">The name of its ExerciseCategory: the challenge draws one exercise per category.</param>
public sealed record ChallengeExercise(int ExerciseId, string Name, string Category);

/// <param name="Exercise">The exercise name, which is also its page: /Exercise/{Exercise}.</param>
/// <param name="Answered">The day's answers to it, right or wrong, up to <paramref name="Target"/>.</param>
public sealed record ChallengeItem(string Exercise, string Category, int Answered, int Target)
{
    public bool Done => Answered >= Target;

    public int Percent => Target <= 0 ? 100 : Answered * 100 / Target;
}

/// <param name="Date">The player's local date.</param>
public sealed record DailyChallengeProgress(DateOnly Date, IReadOnlyList<ChallengeItem> Items)
{
    public bool IsComplete => Items.Count > 0 && Items.All(i => i.Done);
}

/// <summary>
/// The daily challenge: each date draws three exercises from different categories, the same for
/// every player, and a player completes it by answering each of them a few times on that local date.
/// Nothing is stored: the draw only depends on the date and the exercises, and the progress on the
/// player's answers.
/// </summary>
public static class DailyChallengeRules
{
    public const int ExercisesPerDay = 3;

    /// <summary>Answers, right or wrong, that complete one exercise of the challenge.</summary>
    public const int AnswersPerExercise = 5;

    /// <summary>The same for an exercise answered on a staff, where each question takes longer.</summary>
    public const int AnswersPerStaffExercise = 3;

    private static readonly HashSet<string> StaffExercises = new(StringComparer.Ordinal)
    {
        "CompleteChord", "CompleteScale", "MelodicDictation", "RhythmDictation", "TransposeScale",
    };

    public static int Target(string exercise) =>
        StaffExercises.Contains(exercise) ? AnswersPerStaffExercise : AnswersPerExercise;

    /// <summary>
    /// The date's exercises: the pool is shuffled with the date as the seed, and the first categories
    /// to come up give one exercise each, the one whose turn it is on that date. A category's
    /// exercises take turns, in an order that changes from round to round, so none comes up much
    /// less often than the others of its category, nor two days in a row. With fewer categories
    /// than exercises per day, the rest of the shuffle fills the remaining places.
    /// </summary>
    public static IReadOnlyList<ChallengeExercise> Pick(DateOnly date, IEnumerable<ChallengeExercise> exercises) =>
        Draw(date, Pool(exercises));

    /// <summary>The player's progress on the challenge of <paramref name="date"/>.</summary>
    /// <param name="answers">The player's answers, with UTC timestamps; only those on that local date count.</param>
    public static DailyChallengeProgress Evaluate(
        DateOnly date,
        IReadOnlyCollection<ChallengeExercise> exercises,
        IEnumerable<PracticeAnswer> answers,
        TimeZoneInfo timeZone)
    {
        var answersThatDay = answers.Where(a => PracticeStreak.LocalDate(a.Timestamp, timeZone) == date);
        return Progress(date, Pick(date, exercises), CountByName(Names(exercises), answersThatDay));
    }

    /// <summary>The local dates whose challenge the player completed, oldest first.</summary>
    /// <param name="answers">The player's answers, with UTC timestamps.</param>
    public static IReadOnlyList<DateOnly> CompletedDays(
        IReadOnlyCollection<ChallengeExercise> exercises,
        IEnumerable<PracticeAnswer> answers,
        TimeZoneInfo timeZone)
    {
        var names = Names(exercises);
        var pool = Pool(exercises);
        return answers
            .GroupBy(a => PracticeStreak.LocalDate(a.Timestamp, timeZone))
            .Select(day => Progress(day.Key, Draw(day.Key, pool), CountByName(names, day)))
            .Where(progress => progress.IsComplete)
            .Select(progress => progress.Date)
            .Order()
            .ToList();
    }

    private static DailyChallengeProgress Progress(
        DateOnly date, IReadOnlyList<ChallengeExercise> picked, Dictionary<string, int> counts)
    {
        var items = picked
            .Select(e =>
            {
                var target = Target(e.Name);
                return new ChallengeItem(e.Name, e.Category, Math.Min(counts.GetValueOrDefault(e.Name), target), target);
            })
            .ToList();
        return new DailyChallengeProgress(date, items);
    }

    private static Dictionary<int, string> Names(IEnumerable<ChallengeExercise> exercises) =>
        exercises.ToDictionary(e => e.ExerciseId, e => e.Name);

    // An answer counts for its exercise's name, whichever row with that name it was saved under.
    private static Dictionary<string, int> CountByName(Dictionary<int, string> names, IEnumerable<PracticeAnswer> answers)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (names.TryGetValue(answer.ExerciseId, out var name)) counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        return counts;
    }

    /// <summary>
    /// One exercise per name (the lowest id, like the learning path), in a fixed order so the order of
    /// the database rows doesn't change the draw.
    /// </summary>
    private static ChallengeExercise[] Pool(IEnumerable<ChallengeExercise> exercises) => exercises
        .Where(e => !MicrophoneExercises.Contains(e.Name))
        .GroupBy(e => e.Name, StringComparer.Ordinal)
        .Select(g => g.MinBy(e => e.ExerciseId)!)
        .OrderBy(e => e.Name, StringComparer.Ordinal)
        .ToArray();

    private static List<ChallengeExercise> Draw(DateOnly date, ChallengeExercise[] pool)
    {
        var shuffled = Shuffle(pool, (ulong)date.DayNumber);

        var picked = shuffled
            .Select(e => e.Category)
            .Distinct(StringComparer.Ordinal)
            .Take(ExercisesPerDay)
            .Select(category => Turn(category, Array.FindAll(pool, e => string.Equals(e.Category, category, StringComparison.Ordinal)), date.DayNumber))
            .ToList();
        foreach (var exercise in shuffled)
        {
            if (picked.Count == ExercisesPerDay) break;
            if (!picked.Contains(exercise)) picked.Add(exercise);
        }
        return picked;
    }

    /// <summary>
    /// The exercise of <paramref name="category"/> whose turn it is on <paramref name="day"/>. Its
    /// exercises take turns in rounds of as many days as it has exercises, each once a round, in an
    /// order shuffled anew every round with a seed of the category's own: categories of the same
    /// size would otherwise move in step, an exercise of one always drawn with the same exercise of
    /// the other. A round never starts with the exercise that ended the one before.
    /// </summary>
    private static ChallengeExercise Turn(string category, ChallengeExercise[] exercises, int day)
    {
        // Two exercises can only alternate.
        if (exercises.Length <= 2) return exercises[day % exercises.Length];

        var round = day / exercises.Length;
        var order = RoundOrder(category, exercises, round);
        // Swapping the first two leaves the last in place, so the round before ends as shuffled.
        if (order[0] == RoundOrder(category, exercises, round - 1)[^1])
            (order[0], order[1]) = (order[1], order[0]);
        return order[day % exercises.Length];
    }

    private static ChallengeExercise[] RoundOrder(string category, ChallengeExercise[] exercises, int round) =>
        Shuffle(exercises, StableHash(category) ^ unchecked((ulong)round));

    /// <summary>A Fisher-Yates shuffle of a copy of <paramref name="items"/>, from <paramref name="seed"/>.</summary>
    private static T[] Shuffle<T>(T[] items, ulong seed)
    {
        var shuffled = (T[])items.Clone();
        var state = seed;
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = (int)(NextRandom(ref state) % (ulong)(i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    /// <summary>FNV-1a: unlike string.GetHashCode, the same in every process.</summary>
    private static ulong StableHash(string text)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            hash = unchecked((hash ^ b) * 0x100000001B3UL);
        }
        return hash;
    }

    /// <summary>SplitMix64: unlike System.Random, the same sequence on every platform and .NET version.</summary>
    private static ulong NextRandom(ref ulong state)
    {
        unchecked
        {
            var z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
