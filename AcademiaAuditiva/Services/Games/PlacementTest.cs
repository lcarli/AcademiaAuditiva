using AcademiaAuditiva.Services.LearningPath;

namespace AcademiaAuditiva.Services.Games;

/// <summary>One exercise of the placement test, asked <see cref="PlacementTest.QuestionsPerItem"/> times with the learning path step's preset.</summary>
/// <param name="Unit">1-based learning path unit it checks.</param>
public sealed record PlacementItem(int Unit, PathStep Step)
{
    public string Exercise => Step.Exercise;
}

/// <param name="Correct">Right answers given on the unit's items.</param>
/// <param name="Answered">Answers given on them.</param>
/// <param name="Passed">Whether the unit was passed; null while it is being tested or before.</param>
public sealed record PlacementUnitResult(int Unit, int Correct, int Answered, bool? Passed);

/// <summary>Where a placement test stands after its answers.</summary>
/// <param name="ItemIndex">0-based index in <see cref="PlacementTest.Items"/> of the item asked next; -1 once finished.</param>
/// <param name="Question">1-based question of that item asked next; 0 once finished.</param>
/// <param name="SuggestedUnit">The 1-based learning path unit to start at, once finished.</param>
public sealed record PlacementState(
    int ItemIndex,
    int Question,
    int? SuggestedUnit,
    IReadOnlyList<PlacementUnitResult> Units)
{
    public bool Finished => SuggestedUnit is not null;

    public PlacementItem? Current => ItemIndex >= 0 ? PlacementTest.Items[ItemIndex] : null;
}

/// <summary>
/// A short test of the learning path's first units, which suggests where to start on the path.
/// Each unit is checked with three of its exercises, each asked
/// <see cref="QuestionsPerItem"/> times with the step's preset: <see cref="PassMark"/> right
/// answers pass the unit and move on to the next one, and as many wrong answers as fail it end
/// the test there, so a unit takes at most <see cref="QuestionsPerUnit"/> answers. Passing every
/// tested unit suggests the next one.
/// </summary>
public static class PlacementTest
{
    public const int QuestionsPerItem = 3;

    private static readonly (string Unit, string[] Exercises)[] Plan =
    [
        ("FirstSteps", ["GuessInterval", "GuessChords", "GuessDegree"]),
        ("BuildingBlocks", ["GuessQuality", "GuessFunction", "GuessRhythmPattern"]),
    ];

    public static IReadOnlyList<PlacementItem> Items { get; } = BuildItems();

    public static int UnitCount => Plan.Length;

    public static int QuestionsPerUnit => Plan[0].Exercises.Length * QuestionsPerItem;

    /// <summary>Right answers that pass a unit: 7 of 9.</summary>
    public const int PassMark = 7;

    /// <summary>Wrong answers that fail a unit, as no pass is possible after them.</summary>
    public static int FailMark => QuestionsPerUnit - PassMark + 1;

    /// <summary>The highest unit the test can suggest.</summary>
    public static int MaxUnit => UnitCount + 1;

    /// <param name="answers">Whether each answer of the test was right, in order.</param>
    public static PlacementState Evaluate(IReadOnlyList<bool> answers)
    {
        var results = new List<PlacementUnitResult>(UnitCount);
        var next = 0;
        for (var unit = 1; unit <= UnitCount; unit++)
        {
            var correct = 0;
            var wrong = 0;
            bool? passed = null;
            while (next < answers.Count && passed is null)
            {
                if (answers[next++]) correct++;
                else wrong++;
                if (correct >= PassMark) passed = true;
                else if (wrong >= FailMark) passed = false;
            }

            results.Add(new PlacementUnitResult(unit, correct, correct + wrong, passed));
            if (passed is null)
            {
                var asked = correct + wrong;
                var itemIndex = Items.Select((item, index) => (item, index))
                    .Where(x => x.item.Unit == unit)
                    .Skip(asked / QuestionsPerItem)
                    .First().index;
                return new PlacementState(itemIndex, asked % QuestionsPerItem + 1, null, Pad(results));
            }
            if (passed == false)
                return new PlacementState(-1, 0, unit, Pad(results));
        }

        return new PlacementState(-1, 0, MaxUnit, results);
    }

    /// <summary>Exercises asked by the test, so a page can tell whether a placement link fits it.</summary>
    public static bool Asks(string exerciseName) => Items.Any(i => i.Exercise == exerciseName);

    private static List<PlacementUnitResult> Pad(List<PlacementUnitResult> results)
    {
        for (var unit = results.Count + 1; unit <= UnitCount; unit++)
            results.Add(new PlacementUnitResult(unit, 0, 0, null));
        return results;
    }

    private static List<PlacementItem> BuildItems()
    {
        var items = new List<PlacementItem>();
        for (var u = 0; u < Plan.Length; u++)
        {
            var unit = LearningPathCatalog.Units.Single(x => x.Key == Plan[u].Unit);
            foreach (var exercise in Plan[u].Exercises)
                items.Add(new PlacementItem(u + 1, unit.Steps.Single(s => s.Exercise == exercise)));
        }
        return items;
    }
}
