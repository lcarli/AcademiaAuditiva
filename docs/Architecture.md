# Architecture

## Overview

Academia Auditiva is an **ASP.NET Core 10 MVC** application for ear-training
exercises. It runs as a single Container App backed by Azure SQL, with all
secrets in Key Vault and pulled by the app's user-assigned Managed Identity.

## Application layers

```
┌────────────────────────────────────────────────────┐
│  Razor Views (Views/, Areas/Identity/Pages)        │
│      Bootstrap 5 + Web Audio API playback          │
└────────────────────────────┬───────────────────────┘
                             │
┌────────────────────────────▼───────────────────────┐
│  Controllers (HomeController, ExerciseController…) │
└────────────────────────────┬───────────────────────┘
                             │
┌────────────────────────────▼───────────────────────┐
│  Services                                           │
│   • IdentityBootstrapper  — seed roles + admin     │
│   • IExerciseValidator    — strategy per exercise  │
│   • IMusicTheoryService   — note/interval theory   │
│   • UserReportService     — student dashboard stats│
│   • AudioTokenService     — one-shot audio rounds  │
│   • EmailComposer         — localized HTML + text  │
│   • EmailSender (MailKit) — invites + notifications│
│   • RoutineEmails         — routine e-mails, capped│
│   • BackgroundEmailWorker — sends after response   │
│   • Notifier              — site notifications     │
│   • NotificationInbox     — the bell and its page  │
│   • NotificationsJob      — hourly reminders, purge│
└────────────────────────────┬───────────────────────┘
                             │
┌────────────────────────────▼───────────────────────┐
│  EF Core 10 + SQL Server                           │
│   ApplicationDbContext + SQL distributed cache      │
└────────────────────────────────────────────────────┘
```

## Identity model

| Role | Self-assigned? | Capabilities |
|---|---|---|
| **Admin** | No (only one initial admin via `Admin:Email`) | Everything; promote/demote Teacher and Admin; lock, unlock and delete accounts |
| **Teacher** | No (granted by Admin) | CRUD classrooms, invite students, build training routines, dashboards |
| **Student** | Yes (default for new sign-ups) | Practice exercises, view progress, view assigned routines |

Authorization policies (`Program.cs`) enforce role inheritance: Admin
satisfies all policies, Teacher satisfies Teacher+Student, Student
satisfies Student.

## Domain model (current)

- `ApplicationUser` (Identity) — extends with `FirstName`, `LastName`,
  `Language` (for its e-mails) and `RoutineEmailsOff`
- `Exercise`, `ExerciseType`, `ExerciseCategory`, `DifficultyLevel`
- `Score`, `BadgesEarned`, `Badge`, `Subscription`
- `Notification` — what the bell shows a user: its kind, the student and the
  routine assignment or classroom it's about, and when it was created and read

Planned (v2):
- `Classroom`, `ClassroomMember` — Teacher-owned cohorts
- `Routine`, `RoutineItem` — exercise plans assigned to students or
  the whole classroom
- `RoutineAssignment` — who is doing which routine, with per-student
  overrides for class-wide assignments
- `Invite` — pending student invitations (email + token)

## Notifications

`Notifier` (`Services/Notifications/`) writes a `Notification` on four events:

| Event | Who is told | Where |
|---|---|---|
| A routine is assigned | each student it goes to | `RoutinesController.Assign`, after the save, next to the routine e-mails |
| A routine is due the next day | each of its students who hasn't finished it | `NotificationsJob` |
| A student answers the last question of a routine | the routine's teacher | `ExerciseController.ValidateExercise` |
| A student accepts an invite | the classroom's teacher | `InvitesController.Accept` |

- The bell in the top bar (`NotificationBellViewComponent`, in
  `Views/Shared/_Layout.cshtml`) counts the unread ones, up to "9+", when a
  page loads; nothing is pushed. It opens `/Notifications`, which lists the
  newest 100. Opening one marks it read and goes to what it is about: the
  routine on My Training, the student's report or the classroom.
- The texts come from the three `.resx` files when shown, with the current
  names of the routine, classroom and people, so they follow the reader's
  language.
- Notifications are a bonus: `Notifier` logs a failure rather than throw, so
  the assignment, the answer or the invite stands without them.
- `NotificationsJob` runs when the app starts, then every hour, on every
  replica (not in the Testing environment). It deletes the notifications older
  than 90 days, read or not, then reminds students of the routines due the
  next day. A filtered unique index on (`RoutineAssignmentId`, `Kind`,
  `StudentId`) tells each student once, even when replicas race.
- Students' time zones aren't stored, so days are UTC days. Reminders start at
  12:00 UTC (`Notifier.ReminderHourUtc`), when the next UTC day is the next
  day from Canada to France and Brazil, and may come up to an hour later. A
  routine assigned the day before it is due gets no reminder: its students
  were just told of it, with its due date.
- Unassigning a routine deletes its notifications. Deleting an account deletes
  those it received and those about it (`PersonalDataService`), and the data
  export lists the user's own.

## Azure topology

See [Deploy-Azure.md](Deploy-Azure.md) for the diagram and resource list.

## Data flow: secrets

```
   Key Vault (kv-aa-prd-…)  ◀── private endpoint, managed identity
       │  ConnectionStrings--DefaultConnection
       │  Facebook--AppId / AppSecret
       │  Smtp--Host / Port / User / Password / FromAddress
       │  Admin--InitialPassword
       │
       │  (read at app start by the Key Vault configuration provider,
       │   which maps -- to :)
       ▼
   .NET configuration
       Facebook:AppId, Smtp:Host, ConnectionStrings:DefaultConnection, …
```

The Managed Identity has **Key Vault Secrets User** (read-only), plus
**Key Vault Crypto Service Encryption User** on the `dataprotection` key
only, which wraps the ASP.NET Core Data Protection key ring kept in the
`dataprotection-keys` blob container. The human admin has **Key Vault
Secrets Officer** for seeding/rotating.

## Deployment flow

1. Bicep provisions the entire stack (`infra/scripts/deploy-infra.ps1`).
2. Operator runs `seed-keyvault.ps1` to set the optional secrets.
3. Operator runs `grant-mi-sql.ps1` so the MI can run EF migrations.
4. CI builds/tests the app, runs Playwright E2E with SQL Server + Azurite,
   validates Bicep, and scans the container image with Trivy SARIF upload.
5. CD builds the Docker image with `APP_VERSION=<git sha>`, pushes to ACR,
   updates the Container App image/env var, waits until the latest revision
   is ready on that image with 100% traffic, then smoke-tests the versioned
   health endpoints.
6. New revision starts → `Database.Migrate()` creates/updates domain tables
   and `dbo.AppCache` → `IdentityBootstrapper` ensures roles + admin →
   `/health/ready` returns 200.

## Observability

- **Azure Monitor OpenTelemetry distro** exports ASP.NET Core request,
  HttpClient, SQL client, exception, and structured Serilog log telemetry to
  Application Insights when `ApplicationInsights:ConnectionString` is set.
- `/health/live` — process liveness (used by Startup + Liveness probes);
  returns JSON with the running `APP_VERSION`.
- `/health/ready` — readiness with SQL ping (used by Readiness probe);
  also returns `APP_VERSION` so CD can prove the new revision served smoke tests.
- Log Analytics receives Container App stdout via the Container Apps
  Environment integration.

## Scale-out state

Exercise expected answers and audio tokens use `IDistributedCache`. In
production the provider is `Microsoft.Extensions.Caching.SqlServer` backed
by `dbo.AppCache`, whose schema is created by EF migration. The Testing
environment uses the in-memory distributed cache to keep ordinary integration
tests self-contained. ASP.NET Core Data Protection keys are already shared in
Blob Storage and wrapped by Key Vault; sticky sessions remain enabled in
Container Apps to reduce audio-cache churn. Every replica runs
`NotificationsJob`; the database keeps their reminders from doubling up (see
[Notifications](#notifications)).
