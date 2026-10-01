# Deploy to Azure

This guide deploys Academia Auditiva with the Bicep IaC under `infra/`. The
Bicep provisions every resource needed end-to-end: Container Apps, SQL, Key
Vault, Storage, ACR, monitoring, and the private network that connects them.

> **Important — `azd up` is NOT a single-command deploy.** The very first
> deployment is a *multi-phase* process: (1) provision the infra, (2) set the
> optional Key Vault secrets and grant the managed identity SQL access,
> (3) push the real container image, (4) point DNS at the environment and
> issue the certificates. After that, every change to `master` flows through
> CD without manual steps.

## Private networking

The subscription enforces Microsoft's Secure Future Initiative (SFI)
baseline, which turns off public network access on Key Vault, SQL and
Storage. The stack is built that way from the start: the Container Apps
environment runs in a VNet and reaches the three services only through
private endpoints, whose private DNS zones make the usual host names
(`*.vault.azure.net`, `*.database.windows.net`, `*.blob.core.windows.net`)
resolve to private IPs inside the VNet. The app's ingress stays public.

Consequences for operators:

- The Key Vault data plane (`az keyvault secret set`, the portal's Secrets
  blade) and SQL connections only work from inside the VNet. Set secrets
  with `infra/scripts/seed-keyvault.ps1`, which goes through Azure Resource
  Manager instead; `grant-mi-sql.ps1` explains how to reach SQL.
- The VNet of a Container Apps environment is fixed at creation. An
  environment created without it must be deleted (app first, then
  environment) before deploying this template.

## Target topology

```
   GitHub ── docker push ──▶ ACR craaprd… (public)
                                  │ AcrPull (managed identity)
                                  ▼
   user ── https ──▶ ┌─ vnet-aa-prd ─────────────────────────────────┐
                     │ snet-cae  Container Apps ca-aa-prd (min=1)     │
                     │           public ingress, managed certificates │
                     │              │ managed identity id-aa-prd      │
                     │ snet-pe   private endpoints                    │
                     │              ├─▶ Key Vault   secrets, DP key   │
                     │              ├─▶ Azure SQL   AAD-only auth     │
                     │              └─▶ Blob        audio, DP keys    │
                     └────────────────────────────────────────────────┘
   Logs and telemetry go to Log Analytics / Application Insights.
```

## Prerequisites

- An Azure subscription you own (Owner or Contributor + User Access Administrator).
- Azure CLI ≥ 2.60 — `az --version` — with the `containerapp` extension.
- The Azure Bicep CLI — `az bicep upgrade` runs on first use.
- PowerShell 7 for the scripts under `infra/scripts`.

## 1. Sign in and pick the subscription

```powershell
az login --tenant <your-tenant-id>
az account set --subscription <your-subscription-id>
```

## 2. Review the parameters

The deployment makes **you** the AAD admin of the SQL server (no SQL
password, ever). Get your Object ID:

```powershell
az ad signed-in-user show --query id -o tsv
```

Edit `infra/main.parameters.prd.json`:

| Parameter | Meaning |
|---|---|
| `aadAdminObjectId`, `aadAdminLogin`, `aadTenantId` | you, as SQL admin and Key Vault Secrets Officer |
| `appAdminEmail` | account the app creates (or promotes) as its first admin |
| `customDomains` | host names and certificate validation method, see step 10 |
| `vnetAddressPrefix` | optional, defaults to `10.60.0.0/16` |

## 3. Validate the template

```powershell
./infra/scripts/deploy-infra.ps1 -WhatIf
```

## 4. Deploy

Always deploy through `infra/scripts/deploy-infra.ps1` rather than a raw
`az deployment sub create`: it keeps the image that CD deployed and the
custom domain certificates that are already bound.

On a new environment, leave the custom domains out until DNS points at it
(step 10):

```powershell
./infra/scripts/deploy-infra.ps1 -SkipCustomDomains
```

Without a running container app, Bicep deploys its placeholder image; pass
`-Image <registry>/academiaauditiva:<tag>` to start with a real one.

Outputs (printed at the end) include:

| Output | Used for |
|---|---|
| `keyVaultName` | seed-keyvault.ps1 |
| `containerRegistryLoginServer` | docker push target |
| `sqlServerFqdn` / `sqlDatabaseName` | grant-mi-sql.ps1 |
| `containerAppFqdn` | smoke test, `www` CNAME |
| `containerAppEnvironmentStaticIp` | apex A record |
| `customDomainVerificationId` | `asuid` TXT records |

## 5. Set the Key Vault secrets

The deployment writes `ConnectionStrings--DefaultConnection` itself. The
other secrets are optional and the app starts without them:

| Secret | Without it |
|---|---|
| `Facebook--AppId`, `Facebook--AppSecret` | no Facebook sign-in |
| `Smtp--Host`, `Smtp--Port`, `Smtp--User`, `Smtp--Password` | no email: registration shows the confirmation link on screen, password reset can't send its link |
| `Admin--InitialPassword` | the `appAdminEmail` account gets a random password |

```powershell
./infra/scripts/seed-keyvault.ps1 -VaultName <keyVaultName from outputs>
```

The script prompts for each secret; an empty answer keeps the current
value. It writes through Azure Resource Manager, so it needs Contributor on
the vault. The app reads the vault at startup: restart the revision after a
change.

## 6. Grant the Managed Identity access to SQL

The Bicep makes you the AAD admin of the SQL server, but the Container
App's MI is not yet a database user. Grant it:

```powershell
./infra/scripts/grant-mi-sql.ps1 `
  -ServerFqdn <sqlServerFqdn from outputs> `
  -DatabaseName <sqlDatabaseName from outputs> `
  -ManagedIdentityName id-aa-prd
```

The script creates the MI as a contained AAD user with
`db_datareader`, `db_datawriter`, `db_ddladmin`. EF migrations on
startup need `db_ddladmin`. SQL only accepts connections from the VNet:
the script's help shows how to open it to your IP while it runs.

## 7. Build and push the application image

CD does this on every push to `master`. By hand:

```powershell
az acr login --name <containerRegistryLoginServer>
docker build --build-arg APP_VERSION=<git-sha-or-version> -t <containerRegistryLoginServer>/academiaauditiva:v1 .
docker push <containerRegistryLoginServer>/academiaauditiva:v1
```

## 8. Roll a new Container App revision

```powershell
az containerapp update `
  --name ca-aa-prd `
  --resource-group rg-aa-prd `
  --image <containerRegistryLoginServer>/academiaauditiva:v1 `
  --set-env-vars APP_VERSION=<git-sha-or-version>
```

## 9. Smoke test

```powershell
curl https://<containerAppFqdn>/health/live   # 200 OK, JSON includes version
curl https://<containerAppFqdn>/health/ready  # 200 OK once SQL is reachable, JSON includes version
```

Then open the FQDN in a browser, sign in with the bootstrap admin email,
and confirm the dashboard loads.

## 10. Custom domains

`customDomains` lists each host name with the validation method of its free
managed certificate: `HTTP` for the apex domain (A record), `CNAME` for
subdomains. A host name can only be added to the app once its DNS points at
the environment, so on a new environment:

1. Deploy with `-SkipCustomDomains` (step 4).
2. Write the DNS records. The zone can be in another subscription or
   tenant; sign in to both tenants first (`az login --tenant …`).

   ```powershell
   ./infra/scripts/configure-dns.ps1 -DnsSubscriptionId <id> -DnsResourceGroup <rg> -WhatIf
   ./infra/scripts/configure-dns.ps1 -DnsSubscriptionId <id> -DnsResourceGroup <rg>
   ```

   | Record | Value |
   |---|---|
   | `@` A | `containerAppEnvironmentStaticIp` |
   | `asuid` TXT | `customDomainVerificationId` |
   | `www` CNAME | `containerAppFqdn` |
   | `asuid.www` TXT | `customDomainVerificationId` |

3. Once the records resolve, run `./infra/scripts/deploy-infra.ps1` again.
   It adds the host names, issues the certificates and binds them. Later
   deploys find the issued certificates and keep them bound.

Managed certificates renew automatically as long as the records stay in
place.

## CI/CD with GitHub Actions

Once the manual deploy works, automate it with the workflow under
`.github/workflows/cd.yml`. On pushes to `master` that touch the app,
infra, or build files, it first runs the CI workflow (build, xUnit tests
including real SQL Server tests, Playwright E2E against SQL Server + Azurite,
Bicep validation, and report-only Trivy SARIF upload) and deploys only if CI
passes. It builds the image with the git SHA as `APP_VERSION`, pushes it to
ACR, updates the Container App image and `APP_VERSION`, waits until
`latestReadyRevisionName == latestRevisionName` with the new image and 100%
traffic, then smoke-tests `/health/live` and `/health/ready` and verifies the
JSON version matches the SHA. Infrastructure changes are deployed with
`deploy-infra.ps1`. It uses **OIDC**
federation to authenticate to Azure without storing secrets — see
[Configure Federated Identity](#configure-federated-identity-one-time)
below.

### Configure federated identity (one-time)

Use the helper script — it is idempotent (safe to re-run) and creates
the app registration, federated credentials for both `refs/heads/master`
and the `production` deployment environment, and grants RBAC on the
RG and ACR:

```powershell
./infra/scripts/setup-github-oidc.ps1 `
  -SubscriptionId 3dc8ff32-42e4-4152-b194-46b704ed70f2 `
  -ResourceGroup  rg-aa-prd `
  -AcrName        craaprdrmz6b3 `
  -Repo           lcarli/AcademiaAuditiva
```

The script prints the three values you need to set as **GitHub Actions
secrets** on the repository, and the `ACR_NAME` repository **variable**.
You also need to create a deployment environment named `production` under
`Settings → Environments` so the environment-scoped federated credential
is honored by `cd.yml`.

<details><summary>What the script does manually</summary>

```powershell
$AppName  = "github-actions-academiaauditiva-cd"
$Repo     = "lcarli/AcademiaAuditiva"
$AppId    = az ad app create --display-name $AppName --query appId -o tsv
$ObjectId = az ad app show --id $AppId --query id -o tsv
$SpId     = az ad sp create --id $AppId --query id -o tsv

# Federated credential for pushes to master
az ad app federated-credential create --id $AppId --parameters @"
{
  "name": "github-master",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:$Repo:ref:refs/heads/master",
  "audiences": ["api://AzureADTokenExchange"]
}
"@

# Grant the SP Contributor on the resource group
az role assignment create --assignee $SpId --role "Contributor" `
  --scope "/subscriptions/<sub>/resourceGroups/rg-aa-prd"
```

</details>

Then add these GitHub Actions secrets:
- `AZURE_CLIENT_ID` = `$AppId`
- `AZURE_TENANT_ID` = your tenant id
- `AZURE_SUBSCRIPTION_ID` = your subscription id

## Cost estimate

| Resource | SKU | ~CAD/month |
|---|---|---|
| Container Apps | Consumption (1 always-on, 0.5 vCPU / 1 GiB) | $15 |
| Azure SQL | Basic 5 DTU 2 GB | $6 |
| Key Vault | Standard | $0.05 |
| ACR | Basic | $7 |
| Storage | Standard LRS, a few GB | $1 |
| Private endpoints | 3 (Key Vault, SQL, Blob) | $30 |
| Private DNS zones | 3 | $2 |
| Log Analytics + AppInsights | First 5 GB free | $0 |
| **Total** | | **~$60** |
