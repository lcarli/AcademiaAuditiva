namespace AcademiaAuditiva.Interfaces;

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
    /// supplied <paramref name="blobNames"/>).
    /// </summary>
    Task<AudioRound> CreateRoundAsync(
        string userId,
        int exerciseId,
        string expectedAnswerJson,
        IReadOnlyList<string> blobNames,
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
/// </summary>
public sealed record AudioRound(
    string RoundId,
    string ExpectedAnswerJson,
    IReadOnlyList<string> Tokens,
    IReadOnlyDictionary<string, string> TokenToBlob);
