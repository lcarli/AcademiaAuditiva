# Academia Auditiva 🎵

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-10.0-purple)](https://docs.microsoft.com/aspnet/core)
[![Azure Container Apps](https://img.shields.io/badge/Azure-Container%20Apps-0078d4)](https://learn.microsoft.com/azure/container-apps)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Academia Auditiva** is a web-based ear-training platform for musicians and
music students: interactive exercises, a guided learning path, gamified
progress and classrooms for teachers, in three languages (en-US / pt-BR /
fr-CA). It runs at <https://academiaauditiva.com>.

## ✨ Features

- 🎧 **21 exercises**: notes, higher or lower, intervals, scale degrees,
  chords and their quality, inversions, harmonic functions, cadences, chord
  progressions, scale types, Greek modes, missing notes, melodic and rhythmic
  dictation, staff exercises (complete a scale or a chord, transpose a scale)
  and sight-singing. Filters choose the key, scale, octave, level and more.
- 🎸 **Piano, guitar or violin**: every exercise with audio plays on the
  instrument the student picks, in the notes it has (the guitar from its
  low E string, the violin from its G string). The guitar strums chords on
  the shapes a guitarist plays, where on the neck the student picks: open
  chords, barre chords or high on the neck. On the piano, the octave range
  moves the chords. The exercises about chords leave out the violin, which
  plays one note at a time.
- 🧭 **Learning path**: 20 steps in 3 units, from *Higher or Lower* to
  *Guess Note*, with progress and celebrations.
- 📅 **Daily challenge**: three exercises from different categories every
  day, the same for every student, with progress on the dashboard.
- 🏆 **Gamification**: XP, levels, practice streaks and badges.
- 🔎 **Explore**: hear and see any note, interval, chord or scale.
- 🆓 **Free practice**: answers are checked but not saved, and *Show answer*
  reveals the answer.
- 🧑‍🏫 **Teachers, students and admins**: teachers run classrooms, invite
  students by e-mail and assign routines; admins manage roles and can lock,
  unlock or delete accounts.
- 📝 **Routines**: students take a routine like a test: each exercise with the
  number of questions and the filters the teacher set, by a due date that may
  accept late answers. Once assigned, a routine's exercises stay fixed;
  teachers duplicate it to change a copy.
- 📊 **Teacher reports**: per routine, per class and per student, made only
  from the answers given in the teacher's own routines; a student's other
  practice stays private.
- 🛡️ **Answers stay on the server**: the server mixes each round's audio and
  streams it through opaque tokens, so the page never holds the answer
  before the student replies.
- 🌍 **Three languages**, light and dark themes, and guided tours on the first
  visit.
- 🔒 **Privacy**: a real privacy policy, data export and full account deletion.
- ☁️ **Azure**: Bicep IaC provisions Container Apps, SQL, Key Vault, Storage,
  ACR and monitoring; Key Vault, SQL and Storage are reachable only through
  private endpoints. Every credential lives in Key Vault and is read with a
  user-assigned managed identity. `/health/live` and `/health/ready` feed the
  Container App probes, and Azure Monitor OpenTelemetry sends telemetry to
  Application Insights.
- ✅ **Tests**: unit, integration, real SQL Server and Playwright E2E.

## 📚 Documentation

| Doc | What it covers |
|---|---|
| [docs/Run-Locally.md](docs/Run-Locally.md) | Run the app on your workstation |
| [docs/Deploy-Azure.md](docs/Deploy-Azure.md) | Provision and ship to Azure end-to-end |
| [docs/Architecture.md](docs/Architecture.md) | Application layers, identity, Azure topology |
| [docs/Security.md](docs/Security.md) | Threat model, secret rotation runbook |
| [docs/Pedagogia-Exercicios.md](docs/Pedagogia-Exercicios.md) (PT) | Exercise categories, levels and badges |
| [docs/FiltrosPorExercicio.md](docs/FiltrosPorExercicio.md) (PT) | The filters of each exercise |
| [docs/landing-art.md](docs/landing-art.md) | ChatGPT prompts and style guide for the home page illustrations |
| [docs/Backlog.md](docs/Backlog.md) | What is left to do, in priority order |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branches, commits, tests and pull requests |
| [docs/archive/](docs/archive/README.md) | Older plans, the backlog and the v2 smoke test, kept for history |

## ⚡ Quick start

```powershell
# Local (requires the .NET 10 SDK, pinned in global.json; full guide in docs/Run-Locally.md)
cd AcademiaAuditiva
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=AcademiaAuditiva-dev;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "Admin:Email" "you@example.com"
dotnet user-secrets set "Admin:InitialPassword" "Some!Strong-Pwd1"
dotnet run   # http://localhost:5063, sign in with the Admin:* account
# No LocalDB? Use the SQL Server container from docs/Run-Locally.md.
# Audio (any instrument) needs Azurite: docs/Run-Locally.md, step 3.

# Azure (one-time setup, see docs/Deploy-Azure.md for details)
az login --tenant <tenant>
./infra/scripts/deploy-infra.ps1 -SkipCustomDomains
./infra/scripts/seed-keyvault.ps1 -VaultName <kv-name>
./infra/scripts/grant-mi-sql.ps1 -ServerFqdn <sql-fqdn> -DatabaseName sqldb-aa-prd -ManagedIdentityName id-aa-prd  # SQL is private: see the script's help
# Custom domains: configure-dns.ps1, then deploy-infra.ps1 again (Deploy-Azure.md, step 10)
```

## 🏗️ Repository layout

```
AcademiaAuditiva/        ASP.NET Core MVC app
infra/                   Bicep IaC (subscription scope) + helper scripts
Tests/                   xUnit unit/integration tests plus Playwright E2E
docs/                    Operator, architecture and exercise docs; archive/ keeps older ones
scripts/                 Local audio (Azurite) and instrument sample tooling
.github/workflows/       CI (build, tests, Bicep), CD gated on CI, CodeQL, Gitleaks
Directory.*.props        Shared build settings + central NuGet package versions
global.json              Pinned .NET SDK
```

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). In short:

1. Fork & clone, create a feature branch
2. Run tests: `dotnet test`; set `AA_TEST_SQL_CONNECTION` to include the focused SQL Server integration checks locally
3. Open a PR to `master` — CI runs build, xUnit, Playwright E2E, Bicep validation, and Trivy image scanning; CodeQL and Gitleaks scan it too

## License

The code is released under the [MIT License](LICENSE).

Third-party files in this repository keep their own licenses:

| Component | Where | License |
|---|---|---|
| Guitar and violin samples (FluidR3_GM, rendered by midi-js-soundfonts) | `AcademiaAuditiva/Audio/Instruments/` | CC BY 3.0, see its `LICENSE.txt` |
| Inter and Figtree fonts | `AcademiaAuditiva/wwwroot/fonts/` | SIL Open Font License 1.1 |
| Bootstrap, jQuery, jQuery Validation, jQuery Validation Unobtrusive | `AcademiaAuditiva/wwwroot/lib/` | MIT, see each folder |
| VexFlow | `AcademiaAuditiva/wwwroot/js/vexflow.js` | MIT |
| Essentia.js (pitch detection in the sight-singing exercise) | `AcademiaAuditiva/wwwroot/js/dist/` | AGPL-3.0 |

Libraries loaded from CDNs, in `AcademiaAuditiva/Views/Shared/_Layout.cshtml` and
`AcademiaAuditiva/Views/Dashboard/Index.cshtml`, are not part of the repository.
