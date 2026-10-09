using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Routines;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Services.Audio;

/// <inheritdoc />
public sealed class AudioTokenService : IAudioTokenService
{
    private static readonly Regex ClipKeyPattern = new(
        "^[A-Za-z][A-Za-z0-9_-]{0,15}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // 15 min — long enough for a round (Play + a few replays + Validate),
    // short enough that a leaked token expires before it matters.
    private static readonly DistributedCacheEntryOptions RoundTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

    private readonly IDistributedCache _cache;
    private readonly TimeProvider _clock;

    public AudioTokenService(IDistributedCache cache, TimeProvider clock)
    {
        _cache = cache;
        _clock = clock;
    }

    public async Task<AudioRound> CreateRoundAsync(
        string userId,
        int exerciseId,
        string expectedAnswerJson,
        IReadOnlyList<string> blobNames,
        bool free = false,
        string? filterJson = null,
        RoutineQuestion? routine = null,
        int? gameRunId = null,
        IReadOnlyList<string>? clipKeys = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(expectedAnswerJson);
        ArgumentNullException.ThrowIfNull(blobNames);
        if (clipKeys is not null)
        {
            if (clipKeys.Count != blobNames.Count)
            {
                throw new ArgumentException("Clip keys must be parallel to blob names.", nameof(clipKeys));
            }
            if (clipKeys.Any(k => string.IsNullOrWhiteSpace(k) || !ClipKeyPattern.IsMatch(k))
                || clipKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != clipKeys.Count)
            {
                throw new ArgumentException("Clip keys must be distinct safe control names.", nameof(clipKeys));
            }
        }
        var storedClipKeys = clipKeys?.ToArray();

        var roundId = Guid.NewGuid().ToString("N");
        var tokens = new string[blobNames.Count];
        var tokenToBlob = new Dictionary<string, string>(blobNames.Count, StringComparer.Ordinal);

        for (var i = 0; i < blobNames.Count; i++)
        {
            var token = Guid.NewGuid().ToString("N");
            tokens[i] = token;
            tokenToBlob[token] = blobNames[i];
        }

        var issuedAt = _clock.GetUtcNow();
        var round = new RoundEnvelope(
            roundId, expectedAnswerJson, tokenToBlob, free, filterJson, issuedAt,
            routine?.Link.AssignmentId, routine?.Link.ItemId, routine?.Number, gameRunId,
            storedClipKeys);
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

        return new AudioRound(
            roundId, expectedAnswerJson, tokens, tokenToBlob, free, filterJson,
            issuedAt, routine, gameRunId,
            storedClipKeys is null ? null : Array.AsReadOnly(storedClipKeys));
    }

    public async Task<string> IssueTokenAsync(
        string userId,
        string address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(address);

        var token = Guid.NewGuid().ToString("N");
        await _cache.SetStringAsync(
            TokenCacheKey(userId, token),
            JsonConvert.SerializeObject(new TokenPointer(RoundId: null, ExerciseId: 0, Address: address)),
            RoundTtl,
            cancellationToken);
        return token;
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

        if (pointer.Address is not null)
        {
            return pointer.Address;
        }

        if (string.IsNullOrEmpty(pointer.RoundId))
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
            : new AudioRound(
                envelope.RoundId, envelope.ExpectedAnswerJson, envelope.TokenToBlob.Keys.ToArray(), envelope.TokenToBlob,
                envelope.Free, envelope.FilterJson, envelope.IssuedAt,
                RoutineQuestion.From(envelope.RoutineAssignmentId, envelope.RoutineItemId, envelope.RoutineQuestion),
                envelope.GameRunId,
                envelope.ClipKeys is null ? null : Array.AsReadOnly(envelope.ClipKeys));
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

    // Rounds cached before free practice existed have no Free field and stay scored;
    // rounds cached before filters were saved have no FilterJson, those cached
    // before answer times were measured have no IssuedAt, and those cached before
    // routine rounds existed belong to no routine (nor game run), and rounds
    // cached before named clips use the legacy response shape.
    private sealed record RoundEnvelope(
        string RoundId,
        string ExpectedAnswerJson,
        Dictionary<string, string> TokenToBlob,
        bool Free = false,
        string? FilterJson = null,
        DateTimeOffset? IssuedAt = null,
        int? RoutineAssignmentId = null,
        int? RoutineItemId = null,
        int? RoutineQuestion = null,
        int? GameRunId = null,
        string[]? ClipKeys = null);

    // Round tokens point at their round; standalone tokens (IssueTokenAsync)
    // carry the clip address themselves.
    private sealed record TokenPointer(string? RoundId, int ExerciseId, string? Address = null);
}
