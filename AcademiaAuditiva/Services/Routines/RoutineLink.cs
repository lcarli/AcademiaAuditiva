using System.Globalization;

namespace AcademiaAuditiva.Services.Routines;

/// <summary>
/// The routine item a round is played for: the student's assignment and the routine's item.
/// My Training links to the exercise page with both in the query string
/// (<see cref="AssignmentKey"/>, <see cref="ItemKey"/>), and the page sends them with every Play.
/// </summary>
public readonly record struct RoutineLink(int AssignmentId, int ItemId)
{
    public const string AssignmentKey = "routineAssignmentId";
    public const string ItemKey = "routineItemId";

    /// <summary>Null unless both ids are positive.</summary>
    public static RoutineLink? From(int? assignmentId, int? itemId)
        => assignmentId is > 0 && itemId is > 0 ? new RoutineLink(assignmentId.Value, itemId.Value) : null;

    public static RoutineLink? FromQuery(IQueryCollection query)
        => int.TryParse(query[AssignmentKey].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var assignmentId)
           && int.TryParse(query[ItemKey].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            ? From(assignmentId, itemId)
            : null;
}
