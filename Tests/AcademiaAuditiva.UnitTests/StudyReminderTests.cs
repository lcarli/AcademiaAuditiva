using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Push;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.UnitTests;

public sealed class StudyReminderTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"push-{Guid.NewGuid():N}").Options);
    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
    private readonly Clock _clock = new(new DateTimeOffset(2031, 7, 1, 22, 0, 0, TimeSpan.Zero));
    private readonly Sender _sender = new();
    private readonly WebPushOptions _options = new() { Enabled = true };

    [Theory]
    [InlineData("https://web.push.apple.com/Q/token", true)]
    [InlineData("https://fcm.googleapis.com/wp/token", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/token", true)]
    [InlineData("https://wns2.notify.windows.com/w/?token", true)]
    [InlineData("http://web.push.apple.com/token", false)]
    [InlineData("https://web.push.apple.com.evil.test/token", false)]
    [InlineData("https://evil.test@web.push.apple.com/token", false)]
    [InlineData("https://web.push.apple.com:8443/token", false)]
    [InlineData("https://127.0.0.1/token", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    [InlineData("https://web.push.apple.com/token#secret", false)]
    public void OnlyKnownHttpsPushEndpointsAreAccepted(string endpoint, bool allowed)
        => PushSubscriptionInput.IsAllowedEndpoint(endpoint).Should().Be(allowed);

    [Fact]
    public void ConfigurationRequiresMatchingVapidKeysOnlyWhenEnabled()
    {
        new WebPushOptions().IsValid().Should().BeTrue();
        new WebPushOptions { Enabled = true }.IsValid().Should().BeFalse();
        var options = ValidOptions();
        options.IsValid().Should().BeTrue();
        options.PrivateKey = "invalid";
        options.IsValid().Should().BeFalse();
        options = ValidOptions();
        options.Subject = "http://insecure.test";
        options.IsValid().Should().BeFalse();
    }

    [Fact]
    public void SubscriptionValidatesRealP256KeysAndTimeZones()
    {
        var input = ValidInput();
        Validate(input).Should().BeEmpty();
        input.Auth = "invalid";
        Validate(input).Should().NotBeEmpty();
        input = ValidInput();
        input.TimeZoneId = "Mars/Nowhere";
        Validate(input).Should().NotBeEmpty();
        input = ValidInput();
        input.P256dh = WebEncoders.Base64UrlEncode(new byte[65]);
        Validate(input).Should().NotBeEmpty();
        input = ValidInput();
        input.MinuteOfDay = 1440;
        Validate(input).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReminderUsesLocalTimeAndRunsOncePerLocalDay()
    {
        var row = await SubscribeAsync();
        row.MinuteOfDay = 19 * 60 + 1;
        await _db.SaveChangesAsync();
        await RunAsync();
        _sender.Payloads.Should().BeEmpty("it is only 19:00 in Sao Paulo");
        _clock.Now = _clock.Now.AddMinutes(1);
        await RunAsync();
        await RunAsync();
        _sender.Payloads.Should().HaveCount(1);
        using var message = JsonDocument.Parse(_sender.Payloads[0]);
        message.RootElement.GetProperty("url").GetString().Should().Be("/Dashboard");
        message.RootElement.GetProperty("title").GetString().Should().Be("Hora de treinar seu ouvido");
        _clock.Now = _clock.Now.AddDays(1);
        await RunAsync();
        _sender.Payloads.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(2, false)] // 02:00 UTC is still the preceding local day.
    [InlineData(3, true)]  // Local midnight.
    [InlineData(15, true)]
    public async Task AnyAnswerDuringTheLocalDaySuppressesTheReminder(int utcHour, bool skipped)
    {
        var row = await SubscribeAsync();
        _db.ScoreSnapshots.Add(new ScoreSnapshot
        {
            UserId = row.UserId, ExerciseId = 1, IsCorrect = false,
            Timestamp = new DateTime(2031, 7, 1, utcHour, 0, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync();
        await RunAsync();
        _sender.Payloads.Should().HaveCount(skipped ? 0 : 1);
        (await _db.StudyPushSubscriptions.SingleAsync()).LastReminderDate.Should().Be(new DateOnly(2031, 7, 1));
    }

    [Fact]
    public async Task ServerDisabledDoesNotSendOrClaimAnything()
    {
        await SubscribeAsync();
        _options.Enabled = false;
        await RunAsync();
        _sender.Payloads.Should().BeEmpty();
        (await _db.StudyPushSubscriptions.SingleAsync()).LastReminderDate.Should().BeNull();
    }

    [Fact]
    public async Task AnExpiredSubscriptionIsRemoved()
    {
        await SubscribeAsync();
        _sender.Expired = true;
        await RunAsync();
        (await _db.StudyPushSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FailedDeliveryIsRetriedOnlyAfterThePersistedLeaseExpires()
    {
        await SubscribeAsync();
        _sender.FailNext = true;
        await RunAsync();
        await RunAsync();
        _sender.Attempts.Should().Be(1);
        (await _db.StudyPushSubscriptions.SingleAsync()).LastReminderDate.Should().BeNull();
        _clock.Now = _clock.Now.AddMinutes(5);
        await RunAsync();
        _sender.Attempts.Should().Be(2);
        (await _db.StudyPushSubscriptions.SingleAsync()).LastReminderDate.Should().NotBeNull();
    }

    [Theory]
    [InlineData("America/New_York", 2031, 3, 9, 23)]
    [InlineData("America/New_York", 2031, 11, 2, 25)]
    [InlineData("America/Havana", 2031, 3, 9, 23)]
    public void CalendarDayBoundariesHandleDst(string zoneId, int year, int month, int day, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var date = new DateTime(year, month, day);
        var start = StudyReminderDispatcher.StartOfDayUtc(date, zone);
        var end = StudyReminderDispatcher.StartOfDayUtc(date.AddDays(1), zone);
        (end - start).TotalHours.Should().Be(hours);
    }

    [Fact]
    public async Task SpringGapAndRepeatedAutumnHourStillProduceOneReminder()
    {
        var row = await SubscribeAsync();
        row.TimeZoneId = "America/New_York";
        row.MinuteOfDay = 2 * 60 + 30;
        await _db.SaveChangesAsync();
        _clock.Now = new DateTimeOffset(2031, 3, 9, 7, 0, 0, TimeSpan.Zero);
        await RunAsync();
        _sender.Payloads.Should().HaveCount(1, "02:30 does not exist, so the reminder runs at 03:00");
        row = await _db.StudyPushSubscriptions.SingleAsync();
        row.MinuteOfDay = 90;
        await _db.SaveChangesAsync();
        _clock.Now = new DateTimeOffset(2031, 11, 2, 5, 30, 0, TimeSpan.Zero);
        await RunAsync();
        _clock.Now = _clock.Now.AddHours(1);
        await RunAsync();
        _sender.Payloads.Should().HaveCount(2, "the repeated 01:30 must not send a second reminder that day");
    }

    [Fact]
    public async Task DispatcherRestoresCallingCulture()
    {
        await SubscribeAsync();
        var previous = CultureInfo.CurrentUICulture;
        await RunAsync();
        CultureInfo.CurrentUICulture.Should().Be(previous);
    }

    [Fact]
    public async Task SenderUsesAes128GcmAndVapidAndHandlesExpiredSubscriptions()
    {
        var input = ValidInput();
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var sender = new WebPushSender(http, Options.Create(ValidOptions()));
        var subscription = new StudyPushSubscription
        {
            Endpoint = input.Endpoint, P256dh = input.P256dh, Auth = input.Auth
        };
        (await sender.SendAsync(subscription, "{\"body\":\"study\"}", default)).Should().BeTrue();
        handler.Encoding.Should().Be("aes128gcm", "Apple requires the modern content encoding");
        handler.Authentication.Should().Be("vapid");
        handler.Ttl.Should().Be("3600");
        handler.Body.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(handler.Body).Should().NotContain("study", "the notification content is encrypted");
        handler.Status = HttpStatusCode.Gone;
        (await sender.SendAsync(subscription, "study", default)).Should().BeFalse();
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<Lib.Net.Http.WebPush.PushServiceClientException>(
            () => sender.SendAsync(subscription, "study", default));
    }

    private async Task<StudyPushSubscription> SubscribeAsync()
    {
        var user = new ApplicationUser { Id = "student", FirstName = "Test", LastName = "Student", Language = "pt-BR" };
        _db.Add(user);
        var row = new StudyPushSubscription
        {
            UserId = user.Id, EndpointHash = "hash", Endpoint = "https://web.push.apple.com/token",
            TimeZoneId = "America/Sao_Paulo", MinuteOfDay = 19 * 60
        };
        _db.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private Task RunAsync() => new StudyReminderDispatcher(_db, _sender, _clock,
        _services.GetRequiredService<IStringLocalizer<SharedResources>>(),
        Options.Create(_options), NullLogger<StudyReminderDispatcher>.Instance).RunAsync(default);

    internal static WebPushOptions ValidOptions()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return new WebPushOptions
        {
            Enabled = true, Subject = "mailto:push@example.test",
            PublicKey = WebEncoders.Base64UrlEncode([4, .. parameters.Q.X!, .. parameters.Q.Y!]),
            PrivateKey = WebEncoders.Base64UrlEncode(parameters.D!)
        };
    }

    internal static PushSubscriptionInput ValidInput() => new()
    {
        Endpoint = "https://web.push.apple.com/token", P256dh = ValidOptions().PublicKey,
        Auth = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)),
        TimeZoneId = "America/Sao_Paulo", MinuteOfDay = 19 * 60
    };

    private static List<ValidationResult> Validate(PushSubscriptionInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results;
    }

    public void Dispose()
    {
        _db.Dispose();
        _services.Dispose();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Sender : IStudyPushSender
    {
        public List<string> Payloads { get; } = [];
        public bool Expired { get; set; }
        public bool FailNext { get; set; }
        public int Attempts { get; private set; }
        public Task<bool> SendAsync(StudyPushSubscription subscription, string payload, CancellationToken ct)
        {
            Attempts++;
            if (FailNext)
            {
                FailNext = false;
                throw new HttpRequestException("Temporary failure");
            }
            Payloads.Add(payload);
            return Task.FromResult(!Expired);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Encoding { get; private set; }
        public string? Authentication { get; private set; }
        public string? Ttl { get; private set; }
        public byte[] Body { get; private set; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Encoding = request.Content!.Headers.ContentEncoding.Single();
            Body = await request.Content.ReadAsByteArrayAsync(ct);
            Authentication = request.Headers.Authorization?.Scheme;
            Ttl = request.Headers.GetValues("TTL").Single();
            return new HttpResponseMessage(Status) { Content = new StringContent("") };
        }
    }
}
