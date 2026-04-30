namespace AcademiaAuditiva.Areas.Teacher.Services;

using AcademiaAuditiva.Models.Teaching;

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
///   <item>Other fields fall through unchanged.</item>
/// </list>
/// </summary>
public static class RoutineItemResolver
{
    public static EffectiveRoutineItem? Resolve(RoutineItem item, RoutineAssignmentOverride? @override)
    {
        if (@override?.ExcludeItem == true) return null;
        var target = @override?.OverrideTargetCount ?? item.TargetCount;
        return new EffectiveRoutineItem(
            ItemId: item.Id,
            ExerciseId: item.ExerciseId,
            Order: item.Order,
            Target: target);
    }
}

public readonly record struct EffectiveRoutineItem(int ItemId, int ExerciseId, int Order, int Target);
