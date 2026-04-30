using AcademiaAuditiva.Interfaces;
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
/// user attempts return 404.
/// </summary>
[ApiController]
[Route("audio")]
[Authorize]
public sealed class AudioController : ControllerBase
{
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

        // The token service returns "container/blobName" so a single
        // endpoint can stream from either the source container (no-op
        // pass-through for GuessNote) or the mixed container.
        var slash = address.IndexOf('/');
        if (slash <= 0 || slash == address.Length - 1)
        {
            _logger.LogWarning("Malformed audio address resolved from token: {Address}", address);
            return NotFound();
        }

        var container = address[..slash];
        var blobName = address[(slash + 1)..];

        // Defense-in-depth: only allow the two well-known containers.
        if (container is not ("piano-audio" or "piano-audio-mixed"))
        {
            _logger.LogWarning("Audio token resolved to unexpected container: {Container}", container);
            return NotFound();
        }

        // Tokens are one-shot per round and shouldn't be cached by the
        // browser or any intermediate proxy — caching would let two
        // tabs share state we want to keep per-round.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        return await StreamBlobAsync(container, blobName, ct);
    }

    /// <summary>
    /// Authenticated by-name access to the source piano samples. Used
    /// only by the sheet-music exercises (SolfegeMelody, IntervalMelodico)
    /// where the note names are intentionally visible to the learner —
    /// hiding the URL would not add anti-cheat value. Requires login,
    /// rejects path traversal, and only serves the source container.
    /// </summary>
    [HttpGet("{name}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
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

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return await StreamBlobAsync("piano-audio", name, ct);
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
