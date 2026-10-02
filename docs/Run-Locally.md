# Run Locally

This guide describes how to run Academia Auditiva on your workstation for
development and testing. It assumes you have already cloned the repository.

## Prerequisites

- **.NET SDK 10.0** — `dotnet --version` should report `10.x` (the minimum
  version is pinned in `global.json`). Install from
  <https://dotnet.microsoft.com/download/dotnet/10.0>.
- **SQL Server LocalDB** *or* Docker for a SQL Server container.
  LocalDB is bundled with Visual Studio; for VS Code use Docker:

  ```powershell
  docker run -d --name aa-sql -p 1433:1433 `
    -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass1" `
    mcr.microsoft.com/mssql/server:2022-latest
  ```
- **Node.js** is only required for the Playwright E2E suite under
  `Tests/e2e`; the app itself does not need a front-end build.
- **Docker** and the **Azure CLI** (`az`) — only to play the piano audio
  locally (see step 3).

## 1. Configure local secrets

The application reads sensitive configuration from the .NET configuration
system. Use [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets)
so credentials never land in `appsettings.json`:

```powershell
cd AcademiaAuditiva
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
    "Server=(localdb)\\mssqllocaldb;Database=AcademiaAuditiva-dev;Trusted_Connection=True;TrustServerCertificate=True"

# Or, with the Docker container from the prerequisites (use 127.0.0.1, not localhost — see Troubleshooting)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
    "Server=127.0.0.1,1433;Database=AcademiaAuditiva-dev;User Id=sa;Password=YourStrong!Pass1;TrustServerCertificate=True;Encrypt=False"

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
dotnet tool restore   # once: installs the dotnet-ef version pinned in dotnet-tools.json
dotnet ef database update --project AcademiaAuditiva
```

## 3. Enable audio (piano samples)

The exercises play piano samples that are **not** in git: in Azure they live
in the private `piano-audio` blob container. Locally they are served by
[Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite),
the Azure Storage emulator. Without it, *Play* hangs for ~20 s and fails.

Run once (downloads the 84 samples from the live site into the git-ignored
`.local/audio/piano-audio` folder — sign in with an Admin account):

```powershell
./scripts/local-audio.ps1 -DownloadFrom https://academiaauditiva.com
```

The script:

1. downloads the samples (skips files that already exist; `-Force` re-downloads);
2. starts the `aa-azurite` container on `127.0.0.1:10000` (restarts with
   Docker; data kept in the `aa-azurite-data` volume);
3. creates the `piano-audio` and `piano-audio-mixed` containers and uploads
   the samples;
4. sets the user-secret `Storage:ConnectionString` to
   `UseDevelopmentStorage=true`, which makes the app use Azurite instead of
   the managed-identity `Storage:BlobEndpoint`.

Later runs without `-DownloadFrom` just re-upload the local samples (e.g.
after `docker rm aa-azurite`). To go back to no audio, run
`dotnet user-secrets remove "Storage:ConnectionString" --project AcademiaAuditiva`.

## 4. Run the app

```powershell
dotnet run --project AcademiaAuditiva
```

Open <http://localhost:5063> (the `http` launch profile that `dotnet run` uses).
`--launch-profile https` also serves <https://localhost:7253>, and the VS Code
F5 configuration listens on <http://localhost:5000>.
The bootstrap admin user will be created on first launch — sign in with
the credentials you set under `Admin:*`.

## 5. Run the tests

```powershell
dotnet test
```

This runs the `Tests/UnitTests` and `Tests/IntegrationTests` projects.
Most integration tests use EF Core InMemory. A focused SQL Server collection
is skipped unless it can reach real SQL Server:

```powershell
# In one process, without printing the secret connection string:
Push-Location .\AcademiaAuditiva
$cs = ((dotnet user-secrets list) | ? { $_ -like 'ConnectionStrings:DefaultConnection = *' }) -replace '^ConnectionStrings:DefaultConnection = ',''
Pop-Location
$env:AA_TEST_SQL_CONNECTION = $cs
dotnet test Tests\AcademiaAuditiva.IntegrationTests
```

On ARM64 workstations the `mcr.microsoft.com/mssql/server:2022-latest`
container image is not runnable, so `AA_TEST_SQL_CONNECTION` should point
to an existing SQL Server such as the local `aa-sql` or Azure SQL Edge
container. In CI on Ubuntu x64, Testcontainers starts SQL Server
automatically.

## 6. Run the Playwright E2E suite

Start the app with SQL Server and Azurite audio configured, then:

```powershell
cd Tests\e2e
npm ci
$env:E2E_BASE_URL = "http://127.0.0.1:5072"
$env:PW_CHANNEL = "msedge"     # local Windows machine; CI uses bundled Chromium
$env:AA_EMAIL = "<bootstrap-admin-email>"
$env:AA_PASSWORD = "<bootstrap-admin-password>"
npm test
```

The suite checks localized home/catalog/privacy pages, health endpoints,
admin login, registration, and a real-audio GuessNote round.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Connection string 'DefaultConnection' not found` | Run `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."` |
| `A network-related or instance-specific error...` | LocalDB not running. `sqllocaldb start MSSQLLocalDB` |
| Login timeout / connection refused with the Docker container | `localhost` can resolve to IPv6 (`::1`) first; use `Server=127.0.0.1,1433` and check `docker ps` shows `aa-sql` running |
| Port already in use | `dotnet run --project AcademiaAuditiva -- --urls http://localhost:5050` (the launch profile overrides `ASPNETCORE_URLS`) |
| Facebook button missing | `Facebook:AppId` / `Facebook:AppSecret` not set — expected for local dev |
| Emails not sent | Same — `Smtp:*` is optional. Check the Console log for the warning. |
| *Play* spins for ~20 s and no sound plays | No audio storage configured: run `./scripts/local-audio.ps1` (step 3) |
| *Play* stopped working (Azurite container stopped) | `docker start aa-azurite` |
| SQL Server integration tests are skipped | Set `AA_TEST_SQL_CONNECTION` in the same process running `dotnet test` |
| Stuck on EF migration | Drop the DB: `DROP DATABASE [AcademiaAuditiva-dev]` then re-run |
