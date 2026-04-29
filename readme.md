# Academia Auditiva 🎵

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-8.0-purple)](https://docs.microsoft.com/aspnet/core)
[![Azure Container Apps](https://img.shields.io/badge/Azure-Container%20Apps-0078d4)](https://learn.microsoft.com/azure/container-apps)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](#license)

**Academia Auditiva** is a web-based ear-training platform for musicians and
music students. It provides interactive exercises (interval, chord, harmonic
function, melodic dictation), gamified progress, and three-language
localization (en-US / pt-BR / fr-CA).

## ✨ What's new in v2

- ☁️ **Single-command Azure deploy** — Bicep IaC provisions Container Apps,
  SQL, Key Vault, ACR, and monitoring from a fresh tenant
- 🔐 **Zero secrets in source** — every credential lives in Key Vault and
  is consumed by the app via a user-assigned Managed Identity
- 🧑‍🏫 **Teacher / Admin / Student roles** — Teachers run classrooms,
  invite students by email, and assign training routines
- ❤️‍🩹 **Health endpoints** — `/health/live` and `/health/ready` wired
  into Container App probes
- 📈 **Application Insights** auto-instrumentation
- ✅ **Test projects** — `Tests/UnitTests` and `Tests/IntegrationTests`

## 📚 Documentation

| Doc | What it covers |
|---|---|
| [docs/Run-Locally.md](docs/Run-Locally.md) | Run the app on your workstation |
| [docs/Deploy-Azure.md](docs/Deploy-Azure.md) | Provision and ship to Azure end-to-end |
| [docs/Architecture.md](docs/Architecture.md) | Application layers, identity, Azure topology |
| [docs/Security.md](docs/Security.md) | Threat model, secret rotation runbook |
| [Arquitetura.md](Arquitetura.md) (PT) | Original architecture overview |
| [MapaDoProjeto.md](MapaDoProjeto.md) (PT) | Feature roadmap |
| [Pedagogia-Exercicios.md](Pedagogia-Exercicios.md) (PT) | Pedagogy notes |

## ⚡ Quick start

```powershell
# Local
cd AcademiaAuditiva
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=AcademiaAuditiva-dev;Trusted_Connection=True"
dotnet user-secrets set "Admin:Email" "you@example.com"
dotnet user-secrets set "Admin:InitialPassword" "Some!Strong-Pwd1"
dotnet run

# Azure (one-time setup, see docs/Deploy-Azure.md for details)
az login --tenant <tenant>
az account set --subscription <sub>
az deployment sub create --location canadacentral `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.prd.json
./infra/scripts/seed-keyvault.ps1 -VaultName <kv-name>
./infra/scripts/grant-mi-sql.ps1 -ServerFqdn <sql-fqdn> -DatabaseName sqldb-aa-prd -ManagedIdentityName id-aa-prd
```

## 🏗️ Repository layout

```
AcademiaAuditiva/        ASP.NET Core MVC app
infra/                   Bicep IaC (subscription scope) + helper scripts
Tests/                   xUnit unit and integration test projects
docs/                    Operator + architecture docs (this README links to)
.github/workflows/       CI/CD (planned)
```

## 🤝 Contributing

1. Fork & clone, create a feature branch
2. Run tests: `dotnet test`
3. Open a PR to `main` — CI runs `dotnet build` + `dotnet test`

## License

MIT. See `LICENSE` (planned).
