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

    /// <summary>Solfege Melody needs a microphone, which not every player can use.</summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "SolfegeMelody" };

    private static readonly HashSet<string> StaffExercises = new(StringComparer.Ordinal)
    {
        "CompleteChord", "CompleteScale", "MelodicDictation", "RhythmDictation", "TransposeScale",
    };

    public static int Target(string exercise) =>
        StaffExercises.Contains(exercise) ? AnswersPerStaffExercise : AnswersPerExercise;

    /// <summary>
    /// The date's exercises: the pool is shuffled with the date as the seed, then the first exercise
    /// of each category not drawn yet is taken. With fewer categories than exercises per day, the
    /// rest of the shuffle fills the remaining places.
    /// </summary>
    public static IReadOnlyList<ChallengeExercise> Pick(DateOnly date, IEnumerable<ChallengeExercise> exercises)
    {
        // One exercise per name (the lowest id, like the learning path), in a fixed order so the
        // order of the database rows doesn't change the draw.
        var pool = exercises
            .Where(e => !Excluded.Contains(e.Name))
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .Select(g => g.MinBy(e => e.ExerciseId)!)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToArray();

        var state = (ulong)date.DayNumber;
        for (var i = pool.Length - 1; i > 0; i--)
        {
            var j = (int)(NextRandom(ref state) % (ulong)(i + 1));
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var picked = new List<ChallengeExercise>(ExercisesPerDay);
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exercise in pool)
        {
            if (picked.Count == ExercisesPerDay) break;
            if (categories.Add(exercise.Category)) picked.Add(exercise);
        }
        foreach (var exercise in pool)
        {
            if (picked.Count == ExercisesPerDay) break;
            if (!picked.Contains(exercise)) picked.Add(exercise);
        }
        return picked;
    }

    /// <summary>The player's progress on the challenge of <paramref name="date"/>.</summary>
    /// <param name="answers">The player's answers, with UTC timestamps; only those on that local date count.</param>
    public static DailyChallengeProgress Evaluate(
        DateOnly date,
        IReadOnlyCollection<ChallengeExercise> exercises,
        IEnumerable<PracticeAnswer> answers,
        TimeZoneInfo timeZone)
    {
        // An answer counts for its exercise's name, whichever row with that name it was saved under.
        var names = exercises.ToDictionary(e => e.ExerciseId, e => e.Name);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (names.TryGetValue(answer.ExerciseId, out var name)
                && PracticeStreak.LocalDate(answer.Timestamp, timeZone) == date)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        var items = Pick(date, exercises)
            .Select(e =>
            {
                var target = Target(e.Name);
                return new ChallengeItem(e.Name, e.Category, Math.Min(counts.GetValueOrDefault(e.Name), target), target);
            })
            .ToList();
        return new DailyChallengeProgress(date, items);
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
