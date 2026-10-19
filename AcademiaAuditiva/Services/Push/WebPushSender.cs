using System.Net;
using AcademiaAuditiva.Models;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace AcademiaAuditiva.Services.Push;

public interface IStudyPushSender
{
    /// <returns>False when the push service says the subscription has expired.</returns>
    Task<bool> SendAsync(StudyPushSubscription subscription, string payload, CancellationToken ct);
}

public sealed class WebPushSender(HttpClient http, IOptions<WebPushOptions> options) : IStudyPushSender
{
    public async Task<bool> SendAsync(StudyPushSubscription subscription, string payload, CancellationToken ct)
    {
        if (!PushSubscriptionInput.IsAllowedEndpoint(subscription.Endpoint))
            throw new InvalidOperationException("The stored push endpoint is not allowed.");
        var config = options.Value;
        var client = new PushServiceClient(http) { AutoRetryAfter = false };
        using var authentication = new VapidAuthentication(config.PublicKey, config.PrivateKey)
        {
            Subject = config.Subject
        };
        try
        {
            using var suppression = SuppressInstrumentationScope.Begin();
            await client.RequestPushMessageDeliveryAsync(
                new PushSubscription
                {
                    Endpoint = subscription.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = subscription.P256dh,
                        ["auth"] = subscription.Auth
                    }
                },
                new PushMessage(payload) { TimeToLive = 3600, Topic = "daily-study" },
                authentication, ct);
            return true;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
