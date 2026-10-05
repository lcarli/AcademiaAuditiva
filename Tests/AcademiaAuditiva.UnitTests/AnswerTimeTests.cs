using AcademiaAuditiva.Services.Scoring;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="AnswerTime"/>: an answer's time runs from when the round was
/// issued to when it was answered, in whole seconds, capped at five minutes so a round
/// left open doesn't inflate the practice time.
/// </summary>
public class AnswerTimeTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 5, 14, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(42.0, 42)]
    [InlineData(41.6, 42)]
    [InlineData(41.4, 41)]
    [InlineData(0.5, 1)]
    [InlineData(0.0, 0)]
    public void IsTheTimeFromIssueToAnswer_InWholeSeconds(double elapsed, int expected)
    {
        AnswerTime.Seconds(IssuedAt, IssuedAt.AddSeconds(elapsed)).Should().Be(expected);
    }

    [Fact]
    public void IsCappedAtFiveMinutes()
    {
        AnswerTime.Seconds(IssuedAt, IssuedAt.AddMinutes(5)).Should().Be(AnswerTime.MaxSeconds);
        AnswerTime.Seconds(IssuedAt, IssuedAt.AddMinutes(6)).Should().Be(300);
        AnswerTime.Seconds(IssuedAt, IssuedAt.AddHours(3)).Should().Be(300);
    }

    [Fact]
    public void IsZero_WhenTheAnswerSeemsToPrecedeTheRound()
    {
        // Two replicas' clocks can disagree by a little.
        AnswerTime.Seconds(IssuedAt, IssuedAt.AddSeconds(-2)).Should().Be(0);
    }

    [Fact]
    public void IsZero_ForRoundsWithoutAnIssueTime()
    {
        AnswerTime.Seconds(null, IssuedAt).Should().Be(0);
    }
}
