using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace AcademiaAuditiva.Controllers;

/// <summary>
/// Streams the per-round audio asset addressed by an opaque token
/// previously issued by <c>RequestPlay</c>. The browser never sees a
/// note name, blob path, or storage URL — only a GUID. Tokens expire
/// with the round (15 min) and are scoped to the issuing user; cross
/// user attempts return 404. Each response is varied (see
/// <see cref="ClipVariation"/>), so the same clip is never served as the
/// same bytes twice.
/// </summary>
[ApiController]
[Route("audio")]
[Authorize]
public sealed class AudioController : ControllerBase
{
    private const string SourceContainer = "piano-audio";
    private const string MixedContainer = "piano-audio-mixed";

    // The mixer caps a mix at 30 s (≈ 5.3 MB of 44.1 kHz stereo WAV).
    private const long MaxClipBytes = 16 * 1024 * 1024;

    private readonly BlobServiceClient _blobServiceClient;
    private readonly IAudioTokenService _audioTokens;
    private readonly ILogger<AudioController> _logger;

    public AudioController(
        BlobServiceClient blobServiceClient,
        IAudioTokenService audioTokens,
        ILogger<AudioController> logger)
    {
        _blobServiceClient = blobServiceClient;
        _audioTokens = audioTokens;
        _logger = logger;
    }

    /// <summary>
    /// Streams the audio bytes for a one-time round token.
    /// The token must have been issued to the calling user; otherwise
    /// the response is 404 (we deliberately do not distinguish "not
    /// found" from "wrong user" so a probing client cannot enumerate).
    /// </summary>
    [HttpGet("token/{token}")]
    [EnableRateLimiting("AudioToken")]
    public async Task<IActionResult> StreamByToken(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var address = await _audioTokens.ResolveTokenAsync(userId, token, ct);
        if (string.IsNullOrEmpty(address))
        {
            _logger.LogInformation("Audio token resolution failed for user {UserId}.", userId);
            return NotFound();
        }

        // The token service returns "container/blobName".
        var slash = address.IndexOf('/');
        if (slash <= 0 || slash == address.Length - 1)
        {
            _logger.LogWarning("Malformed audio address resolved from token: {Address}", address);
            return NotFound();
        }

        var container = address[..slash];
        var blobName = address[(slash + 1)..];

        // Defense-in-depth: round clips only ever come from the mixer.
        if (container != MixedContainer)
        {
            _logger.LogWarning("Audio token resolved to unexpected container: {Container}", container);
            return NotFound();
        }

        // Tokens are one-shot per round and shouldn't be cached by the
        // browser or any intermediate proxy — caching would let two
        // tabs share state we want to keep per-round.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        var clip = await DownloadClipAsync(container, blobName, ct);
        if (clip is null)
        {
            return NotFound();
        }

        // The same notes must never mean the same bytes (see ClipVariation).
        // Every response is unique, so range requests are not supported. A
        // processed clip keeps its level, since its level is what a round
        // compares: a random gain per clip would change the difference.
        var variation = ClipVariation.CreateRandom();
        if (blobName.StartsWith(AudioMixerService.ProcessedBlobPrefix, StringComparison.Ordinal))
        {
            variation = variation with { Gain = 1f };
        }
        var varied = variation.Apply(clip);
        if (varied is null)
        {
            _logger.LogError("Audio clip {Container}/{Blob} is not a 16-bit PCM WAV; not streaming it.", container, blobName);
            return NotFound();
        }

        return File(varied, "audio/wav");
    }

    /// <summary>
    /// By-name access to the source piano samples, for admins only. No
    /// page uses it (exercises play round tokens); it lets
    /// <c>scripts/local-audio.ps1 -DownloadFrom</c> copy the samples for
    /// local development, since the storage account is reachable only
    /// from the VNet. Students can't use it: a labelled, unvaried sample
    /// is a reference to match round clips against. Rejects path
    /// traversal and only serves the source container.
    /// </summary>
    [HttpGet("{name}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> StreamByName(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
        {
            return BadRequest();
        }

        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is not (".mp3" or ".wav" or ".ogg"))
        {
            return BadRequest();
        }

        // Private: shared caches must not hand an admin-only response to anyone else.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return await StreamBlobAsync(SourceContainer, name, ct);
    }

    private async Task<byte[]?> DownloadClipAsync(string container, string blobName, CancellationToken ct)
    {
        try
        {
            var blobClient = _blobServiceClient.GetBlobContainerClient(container).GetBlobClient(blobName);
            var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            await using var content = download.Value.Content;
            var length = download.Value.Details.ContentLength;
            if (length is < 0 or > MaxClipBytes)
            {
                _logger.LogError("Audio clip {Container}/{Blob} has an unexpected size of {Length} bytes.", container, blobName, length);
                return null;
            }

            var clip = new byte[length];
            await content.ReadExactlyAsync(clip, ct);
            return clip;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<IActionResult> StreamBlobAsync(string container, string blobName, CancellationToken ct)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(container);
            var blobClient = containerClient.GetBlobClient(blobName);
            var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            var contentType = string.IsNullOrWhiteSpace(props.Value.ContentType)
                ? "application/octet-stream"
                : props.Value.ContentType;

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            return File(download.Value.Content, contentType, enableRangeProcessing: true);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return NotFound();
        }
    }
}
