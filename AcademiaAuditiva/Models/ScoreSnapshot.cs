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

        /// <summary>
        /// The exercise's own filters the answer was played with, as a preset
        /// (<see cref="Services.ExerciseFilterPresets.ForAnswer"/>), e.g.
        /// <c>{"keySelect":"D4","scaleTypeSelect":"minor"}</c>. Null when the
        /// exercise has no filters, and on answers saved before they were recorded.
        /// </summary>
        public string? FilterJson { get; set; }

        public const int FilterJsonMaxLength = 256;

        /// <summary>
        /// The routine assignment and item a routine answer belongs to, and which of the
        /// item's questions it answered, counted from 1 (see
        /// <see cref="Services.Routines.RoutineRounds"/>). All three are null for practice
        /// outside routines and for answers saved before routines kept their answers.
        /// There is no foreign key: when an assignment is removed, its answers stay as
        /// ordinary practice.
        /// </summary>
        public int? RoutineAssignmentId { get; set; }

        /// <inheritdoc cref="RoutineAssignmentId"/>
        public int? RoutineItemId { get; set; }

        /// <inheritdoc cref="RoutineAssignmentId"/>
        public int? RoutineQuestion { get; set; }
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
