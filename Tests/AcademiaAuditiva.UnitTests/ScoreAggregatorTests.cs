using AcademiaAuditiva.Services.Scoring;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="ScoreAggregator"/> — the pure helper that decides
/// what the next (correctCount, errorCount, bestScore, currentScore)
/// tuple looks like after grading one attempt. The behaviour matters for
/// every learner record on the site, so the rules are pinned down here
/// rather than implicitly in the controller.
/// </summary>
public class ScoreAggregatorTests
{
    [Fact]
    public void FirstCorrectAttempt_FromZero_Yields_1_0_1()
    {
        var u = ScoreAggregator.Apply(0, 0, 0, isCorrect: true);
        u.CorrectCount.Should().Be(1);
        u.ErrorCount.Should().Be(0);
        u.CurrentScore.Should().Be(1);
        u.BestScore.Should().Be(1);
    }

    [Fact]
    public void FirstWrongAttempt_FromZero_Yields_0_1_0_BestStaysAtZero()
    {
        var u = ScoreAggregator.Apply(0, 0, 0, isCorrect: false);
        u.CorrectCount.Should().Be(0);
        u.ErrorCount.Should().Be(1);
        u.CurrentScore.Should().Be(0); // floored, not -1
        u.BestScore.Should().Be(0);
    }

    [Fact]
    public void StreakOfWrongs_NeverDragsBestScoreNegative()
    {
        var (c, e, b) = (0, 0, 0);
        for (int i = 0; i < 10; i++)
        {
            var u = ScoreAggregator.Apply(c, e, b, isCorrect: false);
            (c, e, b) = (u.CorrectCount, u.ErrorCount, u.BestScore);
            u.CurrentScore.Should().Be(0);
            u.BestScore.Should().Be(0);
        }
        c.Should().Be(0);
        e.Should().Be(10);
    }

    [Fact]
    public void BestScore_RatchetsUp_AndIsNeverLoweredByLaterErrors()
    {
        // Build to (5 correct, 0 error) — best=5.
        var (c, e, b) = (0, 0, 0);
        for (int i = 0; i < 5; i++)
        {
            var u = ScoreAggregator.Apply(c, e, b, isCorrect: true);
            (c, e, b) = (u.CorrectCount, u.ErrorCount, u.BestScore);
        }
        b.Should().Be(5);

        // Then 3 wrong — current dips to 2, but best stays at 5.
        for (int i = 0; i < 3; i++)
        {
            var u = ScoreAggregator.Apply(c, e, b, isCorrect: false);
            (c, e, b) = (u.CorrectCount, u.ErrorCount, u.BestScore);
        }

        c.Should().Be(5);
        e.Should().Be(3);
        b.Should().Be(5); // not lowered
    }

    [Theory]
    [InlineData(10, 3, 7, true,  11, 3, 8,  8)] // gain a point — current beats prev best (7) → best=8
    [InlineData(10, 3, 9, true,  11, 3, 9,  8)] // gain a point — current 8 < prev best 9 → best stays 9
    [InlineData(10, 3, 7, false, 10, 4, 7,  6)] // lose a point — current 6 < best 7 → best stays
    [InlineData( 2, 5, 0, false,  2, 6, 0,  0)] // already underwater — current floored at 0
    public void Apply_TableDriven(
        int prevCorrect, int prevError, int prevBest, bool isCorrect,
        int expectedCorrect, int expectedError, int expectedBest, int expectedCurrent)
    {
        var u = ScoreAggregator.Apply(prevCorrect, prevError, prevBest, isCorrect);
        u.CorrectCount.Should().Be(expectedCorrect);
        u.ErrorCount.Should().Be(expectedError);
        u.BestScore.Should().Be(expectedBest);
        u.CurrentScore.Should().Be(expectedCurrent);
    }

    [Fact]
    public void Apply_IsPure_DoesNotMutateInputsAcrossCalls()
    {
        // Same inputs must produce the same output on every call.
        var a = ScoreAggregator.Apply(3, 1, 2, true);
        var b = ScoreAggregator.Apply(3, 1, 2, true);
        a.Should().Be(b);
    }
}
