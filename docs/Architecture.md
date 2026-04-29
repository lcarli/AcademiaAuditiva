# Architecture

## Overview

Academia Auditiva is an **ASP.NET Core 8 MVC** application for ear-training
exercises. It runs as a single Container App backed by Azure SQL, with all
secrets in Key Vault and pulled by the app's user-assigned Managed Identity.

## Application layers

```
┌────────────────────────────────────────────────────┐
│  Razor Views (Views/, Areas/Identity/Pages)        │
│      Bootstrap 5 + Tone.js for audio synthesis     │
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
│   • UserReportService     — score aggregations     │
│   • EmailSender (MailKit) — invites + notifications│
└────────────────────────────┬───────────────────────┘
                             │
┌────────────────────────────▼───────────────────────┐
│  EF Core 8 + SQL Server                             │
│   ApplicationDbContext (Identity + domain tables)   │
└────────────────────────────────────────────────────┘
```

## Identity model

| Role | Self-assigned? | Capabilities |
|---|---|---|
| **Admin** | No (only one initial admin via `Admin:Email`) | Everything; promote/demote Teacher; manage users |
| **Teacher** | No (granted by Admin) | CRUD classrooms, invite students, build training routines, dashboards |
| **Student** | Yes (default for new sign-ups) | Practice exercises, view progress, view assigned routines |

Authorization policies (`Program.cs`) enforce role inheritance: Admin
satisfies all policies, Teacher satisfies Teacher+Student, Student
satisfies Student.

## Domain model (current)

- `ApplicationUser` (Identity) — extends with `FirstName`, `LastName`
- `Exercise`, `ExerciseType`, `ExerciseCategory`, `DifficultyLevel`
- `Score`, `BadgesEarned`, `Badge`, `Subscription`

Planned (v2):
- `Classroom`, `ClassroomMember` — Teacher-owned cohorts
- `Routine`, `RoutineItem` — exercise plans assigned to students or
  the whole classroom
- `RoutineAssignment` — who is doing which routine, with per-student
  overrides for class-wide assignments
- `Invite` — pending student invitations (email + token)

## Azure topology

See [Deploy-Azure.md](Deploy-Azure.md) for the diagram and resource list.

## Data flow: secrets

```
   Key Vault (kv-aa-prd-…)
       │  ConnectionStrings--DefaultConnection
       │  Facebook--AppId / AppSecret
       │  Smtp--Host / Port / User / Password
       │
       │  (read at app start via keyVaultUrl secret references)
       ▼
   Container App secrets
       │
       │  (mapped to env vars with __ separator)
       ▼
   .NET configuration
       Facebook:AppId, Smtp:Host, ConnectionStrings:DefaultConnection, …
```

The Managed Identity has **Key Vault Secrets User** (read-only). The
human admin has **Key Vault Secrets Officer** for seeding/rotating.

## Deployment flow

1. Bicep provisions the entire stack (`az deployment sub create`).
2. Operator runs `seed-keyvault.ps1` to populate real secret values.
3. Operator runs `grant-mi-sql.ps1` so the MI can run EF migrations.
4. CI builds the Docker image, pushes to ACR, and updates the
   Container App revision.
5. New revision starts → `Database.Migrate()` runs → `IdentityBootstrapper`
   ensures roles + admin → `/health/ready` returns 200 → traffic shifts.

## Observability

- **Application Insights** auto-tracks requests, dependencies, exceptions.
- `/health/live` — process liveness (used by Startup + Liveness probes).
- `/health/ready` — readiness with SQL ping (used by Readiness probe).
- Log Analytics receives Container App stdout via the Container Apps
  Environment integration.
