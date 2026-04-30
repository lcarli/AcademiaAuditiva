using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Models
{
    /// <summary>
    /// Single attempt within an exercise round. Append-only: every call to
    /// <c>ValidateExercise</c> writes one row. Used to power timelines,
    /// charts and per-attempt analytics without mutating aggregate state.
    /// </summary>
    public class ScoreSnapshot
    {
        public long Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public IdentityUser? User { get; set; }

        public int ExerciseId { get; set; }
        public Exercise? Exercise { get; set; }

        public bool IsCorrect { get; set; }
        public int TimeSpentSeconds { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Running totals for a (User, Exercise) pair. One row per pair —
    /// updated in place. Replaces the row-per-attempt running-totals
    /// pattern that lived on <see cref="Score"/>.
    /// </summary>
    public class ScoreAggregate
    {
        /// <summary>Composite PK with <see cref="ExerciseId"/>.</summary>
        public string UserId { get; set; } = string.Empty;
        public IdentityUser? User { get; set; }

        public int ExerciseId { get; set; }
        public Exercise? Exercise { get; set; }

        public int CorrectCount { get; set; }
        public int ErrorCount { get; set; }
        public int BestScore { get; set; }
        public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
    }
}
