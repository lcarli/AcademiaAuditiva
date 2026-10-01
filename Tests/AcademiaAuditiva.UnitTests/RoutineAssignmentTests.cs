using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Models.Teaching;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="RoutineItemResolver"/> — the helper that decides
/// what a single routine item looks like for one specific student once
/// a per-student override is applied to the classroom-wide default — and
/// for <see cref="RoutineItemProgress"/>, which decides when it is done.
/// Both underpin the "My Training" page so their rules need to
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

    [Fact]
    public void Override_FilterPreset_IsMergedOverTheItemPreset()
    {
        // The teacher pins "D minor" for one student while the class
        // practises in C: the key changes, the other keys are inherited.
        var item = NewItem(target: 10);
        item.FilterJson = """{"keySelect":"C","scaleTypeSelect":"minor"}""";
        item.MinScore = 80;
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            OverrideFilterJson = """{"keySelect":"D","melodyLength":"5"}"""
        };

        var eff = RoutineItemResolver.Resolve(item, ovr);

        eff.Should().NotBeNull();
        eff!.Value.Filters.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "D",
            ["scaleTypeSelect"] = "minor",
            ["melodyLength"] = "5",
        });
        eff.Value.MinScore.Should().Be(80);
        eff.Value.Target.Should().Be(10);
    }

    [Fact]
    public void BrokenFilterPresets_AreIgnored_NotThrown()
    {
        // Older rows were typed by hand; a bad preset must not break My Training.
        var item = NewItem(target: 10);
        item.FilterJson = "{ not json";
        var ovr = new RoutineAssignmentOverride
        {
            RoutineItemId = item.Id,
            StudentId = "s1",
            OverrideFilterJson = "[\"keySelect\"]"
        };

        var eff = RoutineItemResolver.Resolve(item, ovr);

        eff.Should().NotBeNull();
        eff!.Value.Filters.Should().BeEmpty();
    }

    [Fact]
    public void Progress_BeforeTheFirstAttempt_HasNoAccuracy()
    {
        var progress = new RoutineItemProgress(Attempts: 0, Correct: 0, Target: 10, MinScore: 80);

        progress.Accuracy.Should().BeNull();
        progress.Done.Should().Be(0);
        progress.Percent.Should().Be(0);
        progress.IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 8, 10, 80, true, 100)]   // exactly the minimum accuracy
    [InlineData(10, 10, 10, null, true, 100)] // no minimum: attempts are enough
    [InlineData(12, 5, 10, null, true, 100)]  // attempts beyond the target still count
    [InlineData(5, 5, 10, 80, false, 50)]     // halfway
    [InlineData(3, 3, 7, null, false, 42)]    // floor(42.9)
    [InlineData(15, 6, 10, 80, false, 99)]    // target reached, accuracy too low
    [InlineData(0, 0, 0, 80, true, 100)]      // a target of 0 asks for nothing
    public void Progress_IsComplete_OnceTheTargetAndMinimumAccuracyAreMet(
        int attempts, int correct, int target, int? minScore, bool complete, int percent)
    {
        var progress = new RoutineItemProgress(attempts, correct, target, minScore);

        progress.IsComplete.Should().Be(complete);
        progress.Percent.Should().Be(percent);
        progress.Done.Should().Be(Math.Min(attempts, target));
    }

    [Fact]
    public void Progress_ComparesTheMinimumExactly_NotTheRoundedAccuracy()
    {
        // 199/250 = 79.6% is displayed as 80% but must not pass a minimum of 80%.
        var progress = new RoutineItemProgress(Attempts: 250, Correct: 199, Target: 10, MinScore: 80);

        progress.Accuracy.Should().Be(80);
        progress.MeetsMinScore.Should().BeFalse();
        progress.IsComplete.Should().BeFalse();
        progress.Percent.Should().Be(99);
    }

    [Fact]
    public void Progress_Accuracy_RoundsHalvesAwayFromZero()
    {
        new RoutineItemProgress(Attempts: 8, Correct: 5, Target: 10, MinScore: null).Accuracy.Should().Be(63); // 62.5%
        new RoutineItemProgress(Attempts: 8, Correct: 1, Target: 10, MinScore: null).Accuracy.Should().Be(13); // 12.5%
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
