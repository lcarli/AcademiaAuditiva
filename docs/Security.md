# Security

## Threat model (summary)

| Threat | Mitigation |
|---|---|
| Secret exposure in source | Key Vault + RBAC; no secrets in `appsettings.json` or migrations |
| Compromised SQL admin | AAD-only auth; no SQL password ever; admin is a human AAD identity |
| Compromised pod → DB pivot | MI has only `db_datareader/db_datawriter/db_ddladmin` on a single DB |
| Compromised MI → KV pivot | MI has Secrets **User** (read-only), not Officer |
| Account takeover | Email confirmation required; bootstrap admin uses random password until forgot-password |
| XSS in user content | Razor encoding by default; no `Html.Raw` on untrusted input |
| CSRF | ASP.NET Core anti-forgery on POST forms |
| Brute-force login | Identity lockout enabled (`LockoutEnabled = true`) |

## Secret inventory (production)

Vault: `kv-aa-prd-rmz6b3` (RG `rg-aa-prd`, region `canadacentral`).
Read access: user-assigned MI `id-aa-prd` (role *Key Vault Secrets User*).
The Container App `ca-aa-prd` mounts each one as a `keyVaultUrl` secret
reference; the runtime env var swaps `--` for `__` to match the .NET
configuration provider (e.g. `Facebook--AppSecret` → `Facebook__AppSecret`
→ `Configuration["Facebook:AppSecret"]`).

| Secret | Bound to (Configuration key) | Source of truth |
|---|---|---|
| `ConnectionStrings--DefaultConnection` | `ConnectionStrings:DefaultConnection` | Generated post-deploy from SQL FQDN + DB name; auth is AAD (`Authentication=Active Directory Default`) |
| `Facebook--AppId` | `Facebook:AppId` | Facebook for Developers → App → Settings → Basic |
| `Facebook--AppSecret` | `Facebook:AppSecret` | same — *Show* the App Secret |
| `Smtp--Host` | `Smtp:Host` | `smtp.gmail.com` |
| `Smtp--Port` | `Smtp:Port` | `587` |
| `Smtp--User` | `Smtp:User` | Gmail address that owns the App Password |
| `Smtp--Password` | `Smtp:Password` | Google → Security → 2FA → App passwords |

Verify the inventory at any time:

```powershell
az keyvault secret list --vault-name kv-aa-prd-rmz6b3 `
   --query "[].{name:name,updated:attributes.updated}" -o table
```

## Secret rotation runbook

> All secrets live in `kv-aa-prd-<suffix>`.

### Rotate a Key Vault secret

```powershell
az keyvault secret set --vault-name <kv> --name <name> --value <new>
# Refresh the Container App so the new secret value is fetched
az containerapp revision restart --name ca-aa-prd --resource-group rg-aa-prd `
   --revision $(az containerapp revision list -n ca-aa-prd -g rg-aa-prd --query "[?properties.active].name" -o tsv)
```

Container Apps re-reads `keyVaultUrl` references on revision restart.

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
