using System.Security.Claims;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcademiaAuditiva.Controllers;

/// <summary>
/// Free exploration: the learner picks a note, interval, chord or scale and
/// hears it, with its notes on a keyboard and a staff. Nothing is scored or
/// stored. Clips are streamed through the same short-lived audio tokens as
/// exercise rounds, so sign-in is required and no sample is ever addressed
/// by name.
/// </summary>
[Authorize]
[AutoValidateAntiforgeryToken]
public class ExploreController : Controller
{
    private readonly ExploreSoundBuilder _sounds;
    private readonly IAudioMixerService _audioMixer;
    private readonly IAudioTokenService _audioTokens;

    public ExploreController(ExploreSoundBuilder sounds, IAudioMixerService audioMixer, IAudioTokenService audioTokens)
    {
        _sounds = sounds;
        _audioMixer = audioMixer;
        _audioTokens = audioTokens;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("Explore")]
    // Legit payloads are ~150 bytes.
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Play([FromBody] ExplorePlayRequest? request)
    {
        if (request is null || !ModelState.IsValid || _sounds.Build(request) is not { } sound)
        {
            return BadRequest();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var mixed = await _audioMixer.MixAsync(sound.Plan, HttpContext.RequestAborted);
        var token = await _audioTokens.IssueTokenAsync(userId, $"{mixed.Container}/{mixed.BlobName}", HttpContext.RequestAborted);

        return Json(new { token, root = sound.Root, notes = sound.Notes, simultaneous = sound.Simultaneous });
    }
}
