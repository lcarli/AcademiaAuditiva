namespace AcademiaAuditiva.Services.Scoring
{
    /// <summary>
    /// Pure math used by <c>ExerciseController.ValidateExercise</c> to roll
    /// the previous (correct, error, best) tuple forward by one attempt.
    /// Extracted as a static helper so the rules — in particular the
    /// non-negative <c>BestScore</c> guard — can be exercised directly in
    /// unit tests without spinning up a controller or an EF context.
    /// </summary>
    public static class ScoreAggregator
    {
        /// <summary>
        /// Apply one attempt to the previous totals.
        /// </summary>
        /// <param name="prevCorrect">Correct count before this attempt.</param>
        /// <param name="prevError">Error count before this attempt.</param>
        /// <param name="prevBest">Best score recorded so far.</param>
        /// <param name="isCorrect">True if the just-graded attempt was right.</param>
        public static ScoreUpdate Apply(int prevCorrect, int prevError, int prevBest, bool isCorrect)
        {
            var correct = prevCorrect + (isCorrect ? 1 : 0);
            var error = prevError + (isCorrect ? 0 : 1);
            // BestScore tracks the largest "lead" the learner has had over
            // their mistakes. Floored at zero so a streak of wrong answers
            // can't drag the figure negative.
            var current = System.Math.Max(0, correct - error);
            var best = System.Math.Max(prevBest, current);
            return new ScoreUpdate(correct, error, best, current);
        }
    }

    public readonly record struct ScoreUpdate(int CorrectCount, int ErrorCount, int BestScore, int CurrentScore);
}
