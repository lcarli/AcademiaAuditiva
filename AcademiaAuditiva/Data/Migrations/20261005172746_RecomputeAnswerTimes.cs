using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecomputeAnswerTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Until now each answer saved the browser's time since the exercise page
            // was opened, so a page visit's answers held running totals (10, 25, 27…)
            // that the dashboard summed. An answer that follows another answer of the
            // same visit keeps only its share: the two values differ by about the time
            // between the two answers, while a new visit starts its count again. Then,
            // as for new answers, a round counts at most five minutes.
            //
            // IntervalMelodico and SolfegeMelody have timed each round on its own since
            // #64; such a row is only mistaken for the same visit when its Play came
            // within five seconds of the previous round's Play, and then loses at most
            // those five seconds.
            foreach (var (table, id) in new[] { ("Scores", "ScoreId"), ("ScoreSnapshots", "Id") })
            {
                migrationBuilder.Sql($@"
WITH [Answers] AS (
    SELECT [TimeSpentSeconds],
        LAG([TimeSpentSeconds]) OVER (PARTITION BY [UserId], [ExerciseId] ORDER BY [Timestamp], [{id}]) AS [PreviousSeconds],
        DATEDIFF(SECOND, LAG([Timestamp]) OVER (PARTITION BY [UserId], [ExerciseId] ORDER BY [Timestamp], [{id}]), [Timestamp]) AS [GapSeconds]
    FROM [{table}]
)
UPDATE [Answers] SET [TimeSpentSeconds] = [TimeSpentSeconds] - [PreviousSeconds]
WHERE [TimeSpentSeconds] >= [PreviousSeconds]
    AND ABS([TimeSpentSeconds] - [PreviousSeconds] - [GapSeconds]) <= 5;

UPDATE [{table}] SET [TimeSpentSeconds] = 300 WHERE [TimeSpentSeconds] > 300;
UPDATE [{table}] SET [TimeSpentSeconds] = 0 WHERE [TimeSpentSeconds] < 0;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The running totals can't be restored; the recomputed times stay.
        }
    }
}
