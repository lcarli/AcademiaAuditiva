using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Models.Teaching;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="RoutineItemResolver"/> — the helper that decides
/// what a single routine item looks like for one specific student once
/// a per-student override is applied to the classroom-wide default.
/// The resolver underpins the "My Training" page so its rules need to
/// be unambiguous.
/// </summary>
public class RoutineAssignmentTests
{
    [Fact]
    public void NoOverride_FallsThroughToDefaults()
    {
        var item = NewItem(target: 10);

        var eff = RoutineItemResolver.Resolve(item, @override: null);

        eff.Should().NotBeNull();
        eff!.Value.Target.Should().Be(10);
        eff.Value.ItemId.Should().Be(item.Id);
        eff.Value.ExerciseId.Should().Be(item.ExerciseId);
        eff.Value.Order.Should().Be(item.Order);
    }

    [Fact]
    public void Override_ExcludeItem_SkipsEntirely()
    {
        var item = NewItem(target: 10);
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            ExcludeItem = true
        };

        RoutineItemResolver.Resolve(item, ovr).Should().BeNull();
    }

    [Fact]
    public void Override_TargetCount_ReplacesDefault()
    {
        var item = NewItem(target: 10);
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            OverrideTargetCount = 25
        };

        var eff = RoutineItemResolver.Resolve(item, ovr);

        eff.Should().NotBeNull();
        eff!.Value.Target.Should().Be(25);
    }

    [Fact]
    public void Override_TargetCountZero_IsRespected_NotTreatedAsAbsent()
    {
        // OverrideTargetCount is int? — a value of 0 is meaningfully
        // different from null and must not fall back to the default.
        var item = NewItem(target: 10);
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            OverrideTargetCount = 0
        };

        var eff = RoutineItemResolver.Resolve(item, ovr);

        eff.Should().NotBeNull();
        eff!.Value.Target.Should().Be(0);
    }

    [Fact]
    public void Override_NoFieldsSet_FallsThroughToDefaults()
    {
        // Empty override (teacher created a row but didn't override anything
        // yet) must behave the same as no override at all.
        var item = NewItem(target: 10);
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1"
        };

        var eff = RoutineItemResolver.Resolve(item, ovr);

        eff.Should().NotBeNull();
        eff!.Value.Target.Should().Be(10);
    }

    [Fact]
    public void Override_ExcludeWins_OverTargetCount()
    {
        // If both ExcludeItem and OverrideTargetCount are set, exclusion
        // should take precedence — the item is dropped entirely, no point
        // computing a target the student will never see.
        var item = NewItem(target: 10);
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            ExcludeItem = true,
            OverrideTargetCount = 99
        };

        RoutineItemResolver.Resolve(item, ovr).Should().BeNull();
    }

    private static RoutineItem NewItem(int target)
        => new()
        {
            Id = 42,
            RoutineId = 7,
            ExerciseId = 3,
            Order = 1,
            TargetCount = target,
            FilterJson = null
        };
}
