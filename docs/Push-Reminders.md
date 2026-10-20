# Installable app and daily push reminders

Academia Auditiva is installable as a Home Screen web app, without an App Store
listing. It remains an online app: the service worker never intercepts requests
or caches authenticated pages, exercise answers or audio.

## iPhone and iPad

Use iOS/iPadOS 16.4 or later over HTTPS. In Safari, choose **Share → Add to Home
Screen** (and **Open as Web App**, if offered). Open Academia Auditiva from that
icon, sign in, then open **Daily study reminders** in the account menu, or
**Manage account → Notifications**. Choose a local time and IANA time zone,
then tap **Activate / save reminder** and allow notifications.

Permission is requested only from that tap. It is never requested on page load.
The app can be closed when a notification arrives. Tapping one opens the
dashboard with the daily challenge. Connectivity, Focus and operating-system
settings affect delivery; this is not an alarm with guaranteed timing.

Each device opts in separately. Its time zone is detected initially, but the
saved time zone does not automatically follow travel: update it on this page.
Reminders can be disabled for the current device, or for all devices from any
browser. Switching accounts on a shared device requires disabling its current
browser subscription first. There is a ten-device limit per account.

## Server configuration

Push is **off by default**. The PWA can be installed without configuring push.
Enable it only after supplying a persistent P-256 VAPID key pair and contact URI:

| Configuration | Value |
|---|---|
| `WebPush:Enabled` | `true` |
| `WebPush:Subject` | A `mailto:` contact or HTTPS contact URI |
| `WebPush:PublicKey` | Base64url uncompressed P-256 public key (65 bytes) |
| `WebPush:PrivateKey` | Base64url P-256 private scalar (32 bytes), a secret |

For local development, use `dotnet user-secrets set --project AcademiaAuditiva`
for these values. Generate a pair with Node's built-in crypto module:

```powershell
node -e "const c=require('node:crypto');const k=c.createECDH('prime256v1');k.generateKeys();console.log('PublicKey='+k.getPublicKey().toString('base64url'));console.log('PrivateKey='+k.getPrivateKey().toString('base64url'));"
```

This command prints a private key: run it in a private terminal, do not capture
it in logs, and never commit the output. Keep the same key pair across restarts
and replicas. Rotating it requires users to unsubscribe and opt in again.
Startup fails explicitly if enabled configuration has invalid or mismatched keys.

Production reads configuration through the existing configuration providers.
With the existing Key Vault provider, use secret names `WebPush--Enabled`,
`WebPush--Subject`, `WebPush--PublicKey`, and `WebPush--PrivateKey`; equivalent
environment variables use double underscores. Never put real keys in
`appsettings.json`. This PR does not provision credentials or enable production.

Apply the `AddStudyPushSubscriptions` EF migration using the normal startup
migration flow. Keep at least one application replica running: an in-process
background job cannot deliver while the host is scaled to zero. The application
already defaults to a minimum of one replica; preserve that deployment setting.
Permit outbound HTTPS to the supported browser push services:
`web.push.apple.com`, `fcm.googleapis.com`,
`updates.push.services.mozilla.com`, and subdomains of `notify.windows.com`.
Other endpoints are rejected, redirects are disabled and outgoing HTTP logging
is suppressed for this client to avoid exposing endpoint capability tokens.

## Delivery and privacy

The job checks subscriptions every minute in bounded batches. Once the saved
local time has passed, it checks `ScoreSnapshots` for any answer during that
device's local calendar day, including wrong answers and all exercise tracks.
Already-studied days are skipped. A late-running server can catch up later the
same day; previous days are not replayed. DST gaps fire on the next available
minute; repeated local hours still get at most one normally completed dispatch.

A persisted, optimistic five-minute lease coordinates replicas. Success or
skipping records the local day. Transient failures are logged without endpoint
tokens and retried after the lease expires. If the host crashes after a provider
accepts the push but before the success is saved, a retry can occur: Web Push
does not provide exactly-once delivery. Stable notification tags and provider
topics collapse duplicates. Push messages expire at the provider after one hour.
An answer arriving after the job's study check cannot recall an in-flight push.
Expired subscriptions (HTTP 404/410) are deleted. Unsupported browsers still
have access to the account-wide opt-out.

Payloads use RFC 8291 `aes128gcm` encryption and VAPID, with no third-party push
SaaS account. Endpoint URLs and encryption keys are private user data: they are
included in personal-data exports and cascade-deleted with the account.
User-facing settings and notifications are localized in English, Brazilian
Portuguese and Canadian French.

## Verification

Run `dotnet build` and `dotnet test`. The tests cover endpoint/key validation,
authenticated antiforgery-protected settings, device ownership, scheduling,
local-day study suppression, DST, retries, expiration and SQL replica races.
The browser suite includes manifest/service-worker and settings-flow checks.

For a real-device smoke test, configure VAPID on an HTTPS deployment, install on
an iPhone, opt in with a time a few minutes ahead, and close the app. Confirm the
notification opens the dashboard. Repeat after answering an exercise to confirm
the reminder is skipped; test opt-out and denied permission as well. Browser
automation cannot prove delivery through Apple's push infrastructure.
