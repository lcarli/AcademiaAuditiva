# Run Locally

This guide describes how to run Academia Auditiva on your workstation for
development and testing. It assumes you have already cloned the repository.

## Prerequisites

- **.NET SDK 8.0** — `dotnet --version` should report `8.x`. Install from
  <https://dotnet.microsoft.com/download/dotnet/8.0>.
- **SQL Server LocalDB** *or* Docker for a SQL Server container.
  LocalDB is bundled with Visual Studio; for VS Code use Docker:

  ```powershell
  docker run -d --name aa-sql -p 1433:1433 `
    -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass1" `
    mcr.microsoft.com/mssql/server:2022-latest
  ```
- **Node.js** is **not** required — the front-end uses CDN-hosted Tone.js
  and Bootstrap directly from `wwwroot/lib`.

## 1. Configure local secrets

The application reads sensitive configuration from the .NET configuration
system. Use [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets)
so credentials never land in `appsettings.json`:

```powershell
cd AcademiaAuditiva
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
    "Server=(localdb)\\mssqllocaldb;Database=AcademiaAuditiva-dev;Trusted_Connection=True;TrustServerCertificate=True"

# Optional: only needed if you want Facebook login locally
dotnet user-secrets set "Facebook:AppId" "<your-test-app-id>"
dotnet user-secrets set "Facebook:AppSecret" "<your-test-app-secret>"

# Optional: only needed if you want emails to actually be sent
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Smtp:Port" "465"
dotnet user-secrets set "Smtp:User" "your-email@example.com"
dotnet user-secrets set "Smtp:Password" "your-app-password"

# Bootstrap admin (first-run admin account)
dotnet user-secrets set "Admin:Email" "you@example.com"
dotnet user-secrets set "Admin:InitialPassword" "Some!Strong-Password1"
```

> **No SMTP, no Facebook? No problem.** The app boots either way:
> Facebook auth simply won't appear, and emails are skipped with a
> warning log instead of throwing.

## 2. Apply EF migrations (or let startup do it)

The app calls `context.Database.Migrate()` on startup, so a fresh DB will
be created automatically. To do it manually:

```powershell
dotnet ef database update --project AcademiaAuditiva
```

## 3. Run the app

```powershell
dotnet run --project AcademiaAuditiva
```

Open <https://localhost:5001> (or the port shown in the console).
The bootstrap admin user will be created on first launch — sign in with
the credentials you set under `Admin:*`.

## 4. Run the tests

```powershell
dotnet test
```

This runs the `Tests/UnitTests` and `Tests/IntegrationTests` projects.
Integration tests use EF Core InMemory provider, so SQL Server is **not**
required.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Connection string 'DefaultConnection' not found` | Run `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."` |
| `A network-related or instance-specific error...` | LocalDB not running. `sqllocaldb start MSSQLLocalDB` |
| Port already in use | Set `ASPNETCORE_URLS=http://localhost:5050` before `dotnet run` |
| Facebook button missing | `Facebook:AppId` / `Facebook:AppSecret` not set — expected for local dev |
| Emails not sent | Same — `Smtp:*` is optional. Check the Console log for the warning. |
| Stuck on EF migration | Drop the DB: `DROP DATABASE [AcademiaAuditiva-dev]` then re-run |
