using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Controllers;

[Authorize]
[Route("[controller]/[action]")]
public sealed class StudyRemindersController(
    ApplicationDbContext db, IOptions<WebPushOptions> options,
    IStringLocalizer<SharedResources> text) : Controller
{
    public sealed class EndpointInput
    {
        [Required, MaxLength(2048)]
        public string Endpoint { get; set; } = "";
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Status([FromBody] EndpointInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = text["Push.Invalid"].Value });
        var hash = Hash(input.Endpoint);
        var subscription = await db.StudyPushSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.EndpointHash == hash && s.UserId == UserId, ct);
        return Json(new
        {
            enabled = subscription != null,
            minuteOfDay = subscription?.MinuteOfDay,
            timeZoneId = subscription?.TimeZoneId
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionInput input, CancellationToken ct)
    {
        if (!options.Value.Enabled) return StatusCode(503, new { error = text["Push.Unavailable"].Value });
        if (!ModelState.IsValid) return BadRequest(new { error = text["Push.Invalid"].Value });
        var hash = Hash(input.Endpoint);
        var subscription = await db.StudyPushSubscriptions.SingleOrDefaultAsync(s => s.EndpointHash == hash, ct);
        // Switching accounts on a shared device requires removing its old browser subscription.
        if (subscription != null && subscription.UserId != UserId)
            return Conflict(new { error = text["Push.OtherAccount"].Value });
        if (subscription == null)
        {
            if (await db.StudyPushSubscriptions.CountAsync(s => s.UserId == UserId, ct) >= 10)
                return Conflict(new { error = text["Push.DeviceLimit"].Value });
            subscription = new StudyPushSubscription { UserId = UserId, EndpointHash = hash };
            db.StudyPushSubscriptions.Add(subscription);
        }
        subscription.Endpoint = input.Endpoint;
        subscription.P256dh = input.P256dh;
        subscription.Auth = input.Auth;
        subscription.TimeZoneId = input.TimeZoneId;
        subscription.MinuteOfDay = input.MinuteOfDay;
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DisableAll(CancellationToken ct)
    {
        var subscriptions = await db.StudyPushSubscriptions.Where(s => s.UserId == UserId).ToListAsync(ct);
        db.StudyPushSubscriptions.RemoveRange(subscriptions);
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe([FromBody] EndpointInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = text["Push.Invalid"].Value });
        var hash = Hash(input.Endpoint);
        var subscription = await db.StudyPushSubscriptions
            .SingleOrDefaultAsync(s => s.EndpointHash == hash && s.UserId == UserId, ct);
        if (subscription != null)
        {
            db.StudyPushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(ct);
        }
        return Ok();
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Hash(string endpoint) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}
