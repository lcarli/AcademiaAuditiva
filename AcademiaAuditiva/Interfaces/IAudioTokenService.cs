namespace AcademiaAuditiva.Interfaces;

using AcademiaAuditiva.Services.Routines;

/// <summary>
/// Issues and resolves opaque per-round audio tokens. The front-end
/// receives only token GUIDs from <c>RequestPlay</c> and uses them in
/// <c>GET /audio/token/{token}</c> to fetch the actual audio bytes.
/// The mapping <c>token → blobName</c> lives only on the server, in
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>,
/// scoped to <c>(userId, exerciseId, roundId)</c> and bound to a 15 min TTL.
///
/// This is the core anti-cheat primitive: even a user with full DevTools
/// cannot map a token back to a note name without compromising the server.
/// </summary>
public interface IAudioTokenService
{
    /// <summary>
    /// Creates a new round for the given user/exercise, persists the
    /// expected answer JSON and the playback token map, and returns the
    /// round identifier together with the issued tokens (parallel to the
    /// supplied <paramref name="blobNames"/>). A <paramref name="free"/>
    /// round (free practice) is checked but never scored, and its answer
    /// may be revealed before it is answered. <paramref name="filterJson"/>
    /// (the exercise filters it was played with) is saved with its answer,
    /// and so are <paramref name="routine"/>, the routine question it asks, and
    /// <paramref name="gameRunId"/>, the game run it belongs to.
    /// </summary>
    Task<AudioRound> CreateRoundAsync(
        string userId,
        int exerciseId,
        string expectedAnswerJson,
        IReadOnlyList<string> blobNames,
        bool free = false,
        string? filterJson = null,
        RoutineQuestion? routine = null,
        int? gameRunId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a token for a clip that belongs to no exercise round (the
    /// Explore page, where the learner picks what to hear, and the starting
    /// note of a sight-singing melody, which is on the staff anyway). It
    /// resolves like a round token for the same 15 min, but there is no
    /// expected answer behind it.
    /// </summary>
    Task<string> IssueTokenAsync(
        string userId,
        string address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a token to the underlying blob name, if it is still valid
    /// and was issued to <paramref name="userId"/>. Returns <c>null</c>
    /// when the token is unknown, expired, or belongs to another user.
    /// </summary>
    Task<string?> ResolveTokenAsync(
        string userId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the cached round, if it exists and matches the user and
    /// exercise. Returns <c>null</c> when expired or not found.
    /// </summary>
    Task<AudioRound?> GetRoundAsync(
        string userId,
        int exerciseId,
        string roundId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the round and all of its tokens. Called by
    /// <c>ValidateExercise</c> after scoring so the same round cannot be
    /// replayed with the same tokens.
    /// </summary>
    Task RemoveRoundAsync(
        string userId,
        int exerciseId,
        string roundId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot of a single exercise round, returned by <see cref="IAudioTokenService"/>.
/// <paramref name="Free"/> marks a free practice round; <paramref name="FilterJson"/> is the
/// preset of exercise filters it was played with (<c>ScoreSnapshot.FilterJson</c>).
/// <paramref name="IssuedAt"/> is when the round was created, which the answer's time is
/// measured from; rounds cached before it was recorded have none. <paramref name="Routine"/>
/// is the routine question the round asks (see <see cref="RoutineRounds"/>), and
/// <paramref name="GameRunId"/> the game run it belongs to (see <see cref="Models.GameRun"/>).
/// </summary>
public sealed record AudioRound(
    string RoundId,
    string ExpectedAnswerJson,
    IReadOnlyList<string> Tokens,
    IReadOnlyDictionary<string, string> TokenToBlob,
    bool Free = false,
    string? FilterJson = null,
    DateTimeOffset? IssuedAt = null,
    RoutineQuestion? Routine = null,
    int? GameRunId = null);
