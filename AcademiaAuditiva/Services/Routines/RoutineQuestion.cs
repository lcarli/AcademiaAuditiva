namespace AcademiaAuditiva.Services.Routines;

/// <summary>
/// One question of a routine item, numbered when it is played: its answer takes that question
/// (<see cref="Models.ScoreSnapshot.RoutineQuestion"/>) or none, so a round is never counted twice.
/// </summary>
public readonly record struct RoutineQuestion(RoutineLink Link, int Number)
{
    /// <summary>Null unless every part is positive.</summary>
    public static RoutineQuestion? From(int? assignmentId, int? itemId, int? number)
        => RoutineLink.From(assignmentId, itemId) is { } link && number is > 0
            ? new RoutineQuestion(link, number.Value)
            : null;
}
