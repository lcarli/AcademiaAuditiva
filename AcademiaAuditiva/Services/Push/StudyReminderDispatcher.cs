using System.Globalization;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Services.Push;

public sealed class StudyReminderDispatcher(
    ApplicationDbContext db, IStudyPushSender sender, TimeProvider clock,
    IStringLocalizer<SharedResources> text, IOptions<WebPushOptions> options,
    ILogger<StudyReminderDispatcher> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        long cursor = 0;
        while (true)
        {
            var now = clock.GetUtcNow();
            var batch = await db.StudyPushSubscriptions.AsNoTracking()
                .Where(s => s.Id > cursor && s.LeaseUntil <= now.UtcDateTime)
                .OrderBy(s => s.Id).Take(100).Select(s => s.Id).ToListAsync(ct);
            if (batch.Count == 0) return;
            foreach (var id in batch)
            {
                cursor = id;
                try
                {
                    await SendIfDueAsync(id, ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another replica claimed, changed or removed this subscription.
                    logger.LogDebug("Push subscription {Id} changed during dispatch", id);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Do not log exception messages: push-provider exceptions contain endpoint tokens.
                    logger.LogError("Study push {Id} failed ({ErrorType}); it will be retried after its lease expires",
                        id, ex.GetType().Name);
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
    }

    private async Task SendIfDueAsync(long id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var subscription = await db.StudyPushSubscriptions.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (subscription == null || subscription.LeaseUntil > now.UtcDateTime) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(subscription.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        if (subscription.LastReminderDate >= today || local.Hour * 60 + local.Minute < subscription.MinuteOfDay) return;

        // Optimistic database claim coordinates replicas without an in-process lock. A crashed
        // sender can retry after five minutes; Web Push cannot guarantee exactly-once delivery.
        subscription.LeaseUntil = now.UtcDateTime.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        var start = StartOfDayUtc(local.Date, zone);
        var end = StartOfDayUtc(local.Date.AddDays(1), zone);
        var studied = await db.ScoreSnapshots.AnyAsync(
            s => s.UserId == subscription.UserId && s.Timestamp >= start && s.Timestamp < end, ct);
        if (!studied)
        {
            var language = await db.Set<ApplicationUser>().Where(u => u.Id == subscription.UserId)
                .Select(u => u.Language).SingleOrDefaultAsync(ct);
            var previousCulture = CultureInfo.CurrentUICulture;
            string payload;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                    language is "pt-BR" or "fr-CA" ? language : "en-US");
                payload = JsonSerializer.Serialize(new
                {
                    title = text["Push.Title"].Value,
                    body = text["Push.Body"].Value,
                    url = "/Dashboard",
                    tag = $"study-{today:yyyy-MM-dd}"
                });
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
            }
            if (!await sender.SendAsync(subscription, payload, ct))
            {
                db.StudyPushSubscriptions.Remove(subscription);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Removed expired study push subscription {Id}", id);
                return;
            }
        }
        subscription.LastReminderDate = today;
        await db.SaveChangesAsync(ct);
    }

    internal static DateTime StartOfDayUtc(DateTime date, TimeZoneInfo zone)
    {
        var midnight = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
        if (zone.IsAmbiguousTime(midnight))
            return DateTime.SpecifyKind(midnight - zone.GetAmbiguousTimeOffsets(midnight).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
