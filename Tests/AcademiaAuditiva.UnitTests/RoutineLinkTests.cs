using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The routine item a round is played for: <see cref="RoutineLink"/>.</summary>
public class RoutineLinkTests
{
    [Fact]
    public void From_LinksBothIds()
    {
        RoutineLink.From(12, 34).Should().Be(new RoutineLink(12, 34));
    }

    [Theory]
    [InlineData(null, 34)]
    [InlineData(12, null)]
    [InlineData(0, 34)]
    [InlineData(12, -1)]
    public void From_IsNull_UnlessBothIdsArePositive(int? assignmentId, int? itemId)
    {
        RoutineLink.From(assignmentId, itemId).Should().BeNull();
    }

    [Theory]
    [InlineData("?routineAssignmentId=12&routineItemId=34")]
    [InlineData("?routineItemId=34&keySelect=D4&routineAssignmentId=12")]
    public void FromQuery_ReadsBothIds(string query)
    {
        FromQuery(query).Should().Be(new RoutineLink(12, 34));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?routineAssignmentId=12")]
    [InlineData("?routineItemId=34")]
    [InlineData("?routineAssignmentId=0&routineItemId=34")]
    [InlineData("?routineAssignmentId=-12&routineItemId=34")]
    [InlineData("?routineAssignmentId=%2B12&routineItemId=34")]
    [InlineData("?routineAssignmentId=%2012&routineItemId=34")]
    [InlineData("?routineAssignmentId=12.0&routineItemId=34")]
    [InlineData("?routineAssignmentId=99999999999&routineItemId=34")]
    [InlineData("?routineAssignmentId=twelve&routineItemId=34")]
    [InlineData("?routineAssignmentId=12&routineAssignmentId=13&routineItemId=34")]
    public void FromQuery_IsNull_UnlessBothIdsAreSinglePositiveWholeNumbers(string query)
    {
        FromQuery(query).Should().BeNull();
    }

    private static RoutineLink? FromQuery(string query)
        => RoutineLink.FromQuery(new QueryCollection(QueryHelpers.ParseQuery(query)));
}
