using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Models;

/// <summary>
/// One play of a game mode (see <see cref="Services.Games.GameModes"/>). The answers are
/// ordinary scored answers tagged with <see cref="ScoreSnapshot.GameRunId"/>;
/// <see cref="Score"/> and <see cref="Answered"/> are recomputed from them after each answer,
/// so the client never reports a score.
/// </summary>
public class GameRun
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public IdentityUser? User { get; set; }

    /// <summary>One of <see cref="Services.Games.GameModes"/>.</summary>
    public string Mode { get; set; } = string.Empty;

    public const int ModeMaxLength = 16;

    /// <summary>The exercise played; null for the placement test, which visits several.</summary>
    public int? ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    /// <summary>The exercise filters the run is played with, as a preset; null for none.</summary>
    public string? FilterJson { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>When the run stopped taking answers; null while it is still going.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Correct answers given in the run.</summary>
    public int Score { get; set; }

    /// <summary>Answers given in the run.</summary>
    public int Answered { get; set; }

    /// <summary>
    /// Placement test only: the learning path unit the student should start at, once the
    /// test is finished.
    /// </summary>
    public int? PlacementUnit { get; set; }

    /// <summary>
    /// Placement test only: when the student chose to skip the learning path up to
    /// <see cref="PlacementUnit"/>.
    /// </summary>
    public DateTime? AppliedAt { get; set; }
}
