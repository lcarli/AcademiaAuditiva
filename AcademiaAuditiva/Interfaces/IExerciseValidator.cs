namespace AcademiaAuditiva.Interfaces
{
    /// <summary>
    /// Result of validating a user guess against the expected answer
    /// for an exercise round.
    /// </summary>
    /// <param name="IsCorrect">True when the user guess matches the expected answer.</param>
    /// <param name="CanonicalAnswer">
    /// Stable string representation of the expected answer used for analytics
    /// (`AttemptDetails.ExpectedAnswer` and the `QuestionId` field). Format is
    /// validator-specific; for compound answers (chord, melodic interval) it
    /// is a `|`-joined token list.
    /// </param>
    public sealed record ExerciseValidationResult(bool IsCorrect, string CanonicalAnswer);

    /// <summary>
    /// One implementation per exercise type. Each validator owns the
    /// JSON-shape produced by <c>MusicTheoryService.GenerateNoteForExercise</c>
    /// for its exercise and knows how to compare the user guess against it.
    /// </summary>
    public interface IExerciseValidator
    {
        /// <summary>
        /// Matches <c>Exercise.Name</c> as seeded in <c>SeedData</c>
        /// (e.g. "GuessNote", "GuessChords", "IntervalMelodico").
        /// </summary>
        string ExerciseName { get; }

        ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson);
    }

    /// <summary>
    /// Resolves the <see cref="IExerciseValidator"/> registered for a given
    /// exercise name. Returns null when no validator is registered so callers
    /// can short-circuit with a 400/404 instead of crashing.
    /// </summary>
    public interface IExerciseValidatorRegistry
    {
        IExerciseValidator? Get(string exerciseName);
    }
}
