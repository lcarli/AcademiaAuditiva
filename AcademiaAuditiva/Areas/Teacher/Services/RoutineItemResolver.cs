namespace AcademiaAuditiva.Areas.Teacher.Services;

using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;

/// <summary>
/// Computes the *effective* shape of a routine item for one student once
/// a personal <see cref="RoutineAssignmentOverride"/> has been applied on
/// top of the classroom-wide <see cref="RoutineItem"/> defaults.
///
/// The rules — extracted from <c>MyTrainingController</c> — are:
/// <list type="bullet">
///   <item>If <see cref="RoutineAssignmentOverride.ExcludeItem"/> is true,
///         the item is skipped entirely (returns null).</item>
///   <item>Otherwise <see cref="RoutineAssignmentOverride.OverrideTargetCount"/>
///         (when present) replaces <see cref="RoutineItem.TargetCount"/>.</item>
///   <item>The override's filter preset is merged over the item's preset;
///         keys set in the override win, the others are inherited.</item>
///   <item>Other fields fall through unchanged.</item>
/// </list>
/// </summary>
public static class RoutineItemResolver
{
    public static EffectiveRoutineItem? Resolve(RoutineItem item, RoutineAssignmentOverride? @override)
    {
        if (@override?.ExcludeItem == true) return null;
        var target = @override?.OverrideTargetCount ?? item.TargetCount;
        var filters = ExerciseFilterPresets.Merge(
            ExerciseFilterPresets.Parse(item.FilterJson),
            ExerciseFilterPresets.Parse(@override?.OverrideFilterJson));
        return new EffectiveRoutineItem(
            ItemId: item.Id,
            ExerciseId: item.ExerciseId,
            Order: item.Order,
            Target: target,
            Filters: filters,
            MinScore: item.MinScore);
    }
}

public readonly record struct EffectiveRoutineItem(
    int ItemId,
    int ExerciseId,
    int Order,
    int Target,
    IReadOnlyDictionary<string, string> Filters,
    int? MinScore);

/// <summary>
/// A student's progress on one routine item, counted from the answers given in the
/// routine (<see cref="AcademiaAuditiva.Services.Routines.RoutineRounds"/>). Like a test,
/// the item is complete once the target number of questions is answered, right or wrong;
/// the optional minimum score (a percentage) is the pass mark of the result.
/// </summary>
public readonly record struct RoutineItemProgress(int Attempts, int Correct, int Target, int? MinScore)
{
    /// <summary>Answers that count towards the target (never more than the target).</summary>
    public int Done => Math.Min(Attempts, Math.Max(Target, 0));

    /// <summary>Rounded accuracy for display, or null before the first answer.</summary>
    public int? Accuracy => Attempts == 0 ? null : (int)Math.Round(100.0 * Correct / Attempts, MidpointRounding.AwayFromZero);

    /// <summary>Exact comparison, so 79.6% does not pass a minimum of 80.</summary>
    public bool MeetsMinScore => MinScore is not int min || (long)Correct * 100 >= (long)min * Attempts;

    public bool IsComplete => Attempts >= Target;

    /// <summary>The share of the questions answered, rounded down.</summary>
    public int Percent => IsComplete || Target <= 0 ? 100 : (int)Math.Floor(100.0 * Done / Target);
}
