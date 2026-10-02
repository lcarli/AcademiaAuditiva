# Security

## Threat model (summary)

| Threat | Mitigation |
|---|---|
| Secret exposure in source | Key Vault + RBAC; no secrets in `appsettings.json` or migrations |
| Compromised SQL admin | AAD-only auth; no SQL password ever; admin is a human AAD identity |
| Compromised pod → DB pivot | MI has only `db_datareader/db_datawriter/db_ddladmin` on a single DB |
| Compromised MI → KV pivot | MI has Secrets **User** (read-only), not Officer; its only key right is wrap/unwrap on the `dataprotection` key |
| Account takeover | Email confirmation required; bootstrap admin gets `Admin--InitialPassword` or a random password (reset with forgot-password) |
| XSS in user content | Razor encoding by default; no `Html.Raw` on untrusted input |
| CSRF | ASP.NET Core anti-forgery on POST forms |
| Brute-force login | Identity lockout enabled (`LockoutEnabled = true`) |
| Abusive or compromised account | An admin can lock the account (no sign-in until unlocked) or delete it in **Admin › Users**; the sign-in cookie is re-checked against the account every minute (`SecurityStampValidatorOptions.ValidationInterval`), and a locked account fails that check and can't refresh its sign-in (`LockoutAwareSignInManager`), so open sessions end within a minute. Admins can't lock or delete their own account here, and other admins must lose the Admin role first |
| Forged scores | Scores are only recorded by `ValidateExercise` against the server-held answer of a one-shot round; no endpoint accepts client-supplied counts |
| DoS via exercise filters | `noteRange` is parsed defensively and clamped to C1–C6; `RequestPlay` bodies are capped at 8 KB and rate-limited |
| Cookie keys lost on deploy / not shared across replicas | Data Protection key ring persisted in blob container `dataprotection-keys`, wrapped by Key Vault key `dataprotection` |
| Exercise round state lost across replicas | `IDistributedCache` uses SQL Server (`dbo.AppCache`) outside Testing, so audio tokens and expected answers are shared |
| Wrong scheme behind the TLS-terminating ingress | `UseForwardedHeaders` honours `X-Forwarded-Proto`/`-For`, so links, OAuth redirects, secure cookies and HSTS see https |
| Known-vulnerable dependencies / base images | `dotnet list package --vulnerable --include-transitive` kept clean; jquery-validation self-hosted (no pinned CDN copy); CI uploads Trivy SARIF for HIGH/CRITICAL container findings with unfixed issues ignored initially |
| Data services reachable from the internet | Key Vault, SQL and Storage have public network access disabled; the app reaches them through private endpoints in its VNet |

## Secret inventory (production)

Vault: `kv-aa-prd-rmz6b3` (RG `rg-aa-prd`, region `canadacentral`).
Read access: user-assigned MI `id-aa-prd` (role *Key Vault Secrets User*).
The app loads every secret at startup with the Key Vault configuration
provider, through the vault's private endpoint. The provider maps `--` to
`:` (e.g. `Facebook--AppSecret` → `Configuration["Facebook:AppSecret"]`).

| Secret | Bound to (Configuration key) | Source of truth |
|---|---|---|
| `ConnectionStrings--DefaultConnection` | `ConnectionStrings:DefaultConnection` | Written by the Bicep deployment from the SQL FQDN, DB name and MI client id; auth is AAD (`Authentication=Active Directory Default`) |
| `Facebook--AppId` | `Facebook:AppId` | Facebook for Developers → App → Settings → Basic |
| `Facebook--AppSecret` | `Facebook:AppSecret` | same — *Show* the App Secret |
| `Smtp--Host` | `Smtp:Host` | `smtp.gmail.com` |
| `Smtp--Port` | `Smtp:Port` | `587` |
| `Smtp--User` | `Smtp:User` | Gmail address that owns the App Password |
| `Smtp--Password` | `Smtp:Password` | Google → Security → 2FA → App passwords |
| `Admin--InitialPassword` | `Admin:InitialPassword` | Operator; only used to create the `Admin__Email` account if it doesn't exist |

The vault's data plane only answers inside the VNet, so list the inventory
through Azure Resource Manager (names and dates, never values):

```powershell
$kv = az keyvault show --name kv-aa-prd-rmz6b3 --query id -o tsv
az rest --method get --url "https://management.azure.com$kv/secrets?api-version=2023-07-01" `
   --query "value[].{name:name,updated:properties.attributes.updated}" -o table
```

## Secret rotation runbook

> All secrets live in `kv-aa-prd-<suffix>`.

### Rotate a Key Vault secret

```powershell
./infra/scripts/seed-keyvault.ps1 -VaultName <kv>   # empty answers keep the other values
# The app reads Key Vault at startup: restart the revision
az containerapp revision restart --name ca-aa-prd --resource-group rg-aa-prd `
   --revision $(az containerapp revision list -n ca-aa-prd -g rg-aa-prd --query "[?properties.active].name" -o tsv)
```

### Rotate Facebook AppSecret

1. Facebook Developers → App → Settings → Basic → Reset App Secret.
2. Update KV: `Facebook--AppSecret`.
3. Restart the Container App revision.

### Rotate Gmail / SMTP app password

1. Google account → Security → 2-Step Verification → App passwords.
2. Generate a new password; revoke the old one immediately.
3. Update KV: `Smtp--Password`.
4. Restart the Container App revision.

### Rotate SQL credentials

There is **no SQL password to rotate** — auth is AAD-only.
- To remove a compromised principal, drop them from the SQL admin
  group, then run `DROP USER [<principal>]` in the database.
- The Container App's MI cannot be "rotated"; if it must be replaced,
  delete the MI in Bicep, redeploy, then re-run `grant-mi-sql.ps1`.

## Known secrets that have leaked into git history

These values appear in commits prior to the security cleanup:

- Facebook `AppSecret` (commit `Program.cs:64-65` history) — **rotated**
- Bootstrap admin password `Lorenzo*181013` (`ApplicationDbContext.cs:42`
  history, plus 11 EF migrations) — **migration `RemoveAdminSeedHardcodedPassword`
  deletes the seeded user; runtime bootstrap creates a fresh admin**
- Gmail app password `dqnszabfutbuouev` (`EmailSender.cs:31` history) — **rotated**

Per project convention, history is **not** rewritten. The values are
no longer accepted by the providers.

## Reporting a vulnerability

Email the maintainer at the bootstrap admin address. Do not open a
public GitHub issue for security concerns.
