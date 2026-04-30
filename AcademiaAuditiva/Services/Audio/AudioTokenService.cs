using AcademiaAuditiva.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace AcademiaAuditiva.Services.Audio;

/// <inheritdoc />
public sealed class AudioTokenService : IAudioTokenService
{
    // 15 min — long enough for a round (Play + a few replays + Validate),
    // short enough that a leaked token expires before it matters.
    private static readonly DistributedCacheEntryOptions RoundTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

    private readonly IDistributedCache _cache;

    public AudioTokenService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<AudioRound> CreateRoundAsync(
        string userId,
        int exerciseId,
        string expectedAnswerJson,
        IReadOnlyList<string> blobNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(expectedAnswerJson);
        ArgumentNullException.ThrowIfNull(blobNames);

        var roundId = Guid.NewGuid().ToString("N");
        var tokens = new string[blobNames.Count];
        var tokenToBlob = new Dictionary<string, string>(blobNames.Count, StringComparer.Ordinal);

        for (var i = 0; i < blobNames.Count; i++)
        {
            var token = Guid.NewGuid().ToString("N");
            tokens[i] = token;
            tokenToBlob[token] = blobNames[i];
        }

        var round = new RoundEnvelope(roundId, expectedAnswerJson, tokenToBlob);
        var roundJson = JsonConvert.SerializeObject(round);

        // Persist the round itself (lookup by user+exercise+round)…
        await _cache.SetStringAsync(
            RoundCacheKey(userId, exerciseId, roundId),
            roundJson,
            RoundTtl,
            cancellationToken);

        // …and the per-token reverse map so the audio endpoint can resolve
        // a bare token (without requiring exerciseId in the URL).
        var tokenPointer = new TokenPointer(roundId, exerciseId);
        var tokenPointerJson = JsonConvert.SerializeObject(tokenPointer);
        foreach (var token in tokens)
        {
            await _cache.SetStringAsync(
                TokenCacheKey(userId, token),
                tokenPointerJson,
                RoundTtl,
                cancellationToken);
        }

        return new AudioRound(roundId, expectedAnswerJson, tokens, tokenToBlob);
    }

    public async Task<string?> ResolveTokenAsync(
        string userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        var pointerJson = await _cache.GetStringAsync(TokenCacheKey(userId, token), cancellationToken);
        if (string.IsNullOrEmpty(pointerJson))
        {
            return null;
        }

        TokenPointer? pointer;
        try
        {
            pointer = JsonConvert.DeserializeObject<TokenPointer>(pointerJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (pointer is null)
        {
            return null;
        }

        var round = await LoadRoundAsync(userId, pointer.ExerciseId, pointer.RoundId, cancellationToken);
        if (round is null)
        {
            return null;
        }

        return round.TokenToBlob.TryGetValue(token, out var blob) ? blob : null;
    }

    public async Task<AudioRound?> GetRoundAsync(
        string userId,
        int exerciseId,
        string roundId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roundId))
        {
            return null;
        }

        var envelope = await LoadRoundAsync(userId, exerciseId, roundId, cancellationToken);
        return envelope is null
            ? null
            : new AudioRound(envelope.RoundId, envelope.ExpectedAnswerJson, envelope.TokenToBlob.Keys.ToArray(), envelope.TokenToBlob);
    }

    public async Task RemoveRoundAsync(
        string userId,
        int exerciseId,
        string roundId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roundId))
        {
            return;
        }

        var envelope = await LoadRoundAsync(userId, exerciseId, roundId, cancellationToken);
        if (envelope is not null)
        {
            foreach (var token in envelope.TokenToBlob.Keys)
            {
                await _cache.RemoveAsync(TokenCacheKey(userId, token), cancellationToken);
            }
        }

        await _cache.RemoveAsync(RoundCacheKey(userId, exerciseId, roundId), cancellationToken);
    }

    private async Task<RoundEnvelope?> LoadRoundAsync(
        string userId,
        int exerciseId,
        string roundId,
        CancellationToken cancellationToken)
    {
        var json = await _cache.GetStringAsync(RoundCacheKey(userId, exerciseId, roundId), cancellationToken);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<RoundEnvelope>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RoundCacheKey(string userId, int exerciseId, string roundId)
        => $"ExerciseRound:{userId}:{exerciseId}:{roundId}";

    private static string TokenCacheKey(string userId, string token)
        => $"AudioToken:{userId}:{token}";

    private sealed record RoundEnvelope(
        string RoundId,
        string ExpectedAnswerJson,
        Dictionary<string, string> TokenToBlob);

    private sealed record TokenPointer(string RoundId, int ExerciseId);
}
