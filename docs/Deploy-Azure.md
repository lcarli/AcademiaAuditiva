# Deploy to Azure

This guide deploys Academia Auditiva to a fresh Azure tenant using the
Bicep IaC under `infra/`. The Bicep provisions every resource needed
end-to-end (Container Apps + SQL + Key Vault + ACR + Monitoring).

> **Important — `azd up` is NOT a single-command deploy.** The very first
> deployment is a *three-phase* process: (1) provision the infra, (2) seed
> Key Vault and grant the managed identity SQL access, (3) push the real
> container image and let the Container App pick it up. Phases 2 and 3 are
> deliberately manual the first time so secrets can be entered interactively
> and the SQL admin role is exercised by a human. After that, every
> subsequent change to `master` flows through CD without manual steps.

## Target topology

```
                 ┌──────────────────────────┐
   GitHub ──▶── │  ACR  craaprd…           │ ◀── docker push
                 └────────┬─────────────────┘
                          │ AcrPull (MI)
                          ▼
   user ──▶ ─── ┌──────────────────────────┐
                │  Container Apps          │
                │   ca-aa-prd  (min=1)     │── Managed Identity ──▶ Key Vault
                └────────┬────────┬────────┘                       (Secrets User)
                         │        │
                         │        └────▶ App Insights / Log Analytics
                         │
                         ▼ AAD-only auth (Active Directory Default + MI clientId)
                ┌──────────────────────────┐
                │  Azure SQL  sqldb-aa-prd │
                └──────────────────────────┘
```

## Prerequisites

- An Azure subscription you own (Owner or Contributor + User Access Administrator).
- Azure CLI ≥ 2.60 — `az --version`.
- The Azure Bicep CLI — `az bicep upgrade` runs on first use.
- (Optional) `azd` 1.10+ if you prefer the Developer CLI experience.

## 1. Sign in and pick the subscription

```powershell
az login --tenant <your-tenant-id>
az account set --subscription <your-subscription-id>
```

## 2. Provide your AAD Object ID

The deployment makes **you** the AAD admin of the SQL server (no SQL
password, ever). Get your Object ID:

```powershell
az ad signed-in-user show --query id -o tsv
```

Edit `infra/main.parameters.prd.json` and set the `aadAdminObjectId` and
`aadAdminLogin` fields. Also confirm `aadTenantId` matches your tenant.

## 3. Validate the template

```powershell
az bicep build --file infra/main.bicep
az deployment sub what-if `
  --location canadacentral `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.prd.json
```

You should see **+ 11 resources** to create.

## 4. Deploy

```powershell
az deployment sub create `
  --location canadacentral `
  --name aa-prd-bootstrap `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.prd.json
```

Outputs (printed at the end) include:

| Output | Used for |
|---|---|
| `keyVaultName` | seed-keyvault.ps1 |
| `keyVaultUri` | sanity check |
| `containerRegistryLoginServer` | docker push target |
| `sqlServerFqdn` / `sqlDatabaseName` | grant-mi-sql.ps1 |
| `containerAppFqdn` | initial smoke test |
| `managedIdentityClientId` | local debugging only |

## 5. Seed Key Vault secrets

The Bicep creates **placeholder** secrets so the Container App can wire
references on first deploy. Fill in the real values now:

```powershell
./infra/scripts/seed-keyvault.ps1 -VaultName <keyVaultName from outputs>
```

The script prompts for each secret (Facebook, SMTP, ConnectionString)
interactively. Re-run anytime to rotate values.

> Build the SQL connection string as:
> `Server=tcp:<sqlServerFqdn>,1433;Initial Catalog=<sqlDatabaseName>;Encrypt=True;Authentication=Active Directory Default;User Id=<managedIdentityClientId>`

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
startup need `db_ddladmin`.

## 7. Build and push the application image

```powershell
az acr login --name <containerRegistryLoginServer>
docker build -t <containerRegistryLoginServer>/academiaauditiva:v1 .
docker push <containerRegistryLoginServer>/academiaauditiva:v1
```

## 8. Roll a new Container App revision

```powershell
az containerapp update `
  --name ca-aa-prd `
  --resource-group rg-aa-prd `
  --image <containerRegistryLoginServer>/academiaauditiva:v1
```

## 9. Smoke test

```powershell
curl https://<containerAppFqdn>/health/live   # 200 OK
curl https://<containerAppFqdn>/health/ready  # 200 OK once SQL is reachable
```

Then open the FQDN in a browser, sign in with the bootstrap admin email,
and confirm the dashboard loads.

## CI/CD with GitHub Actions

Once the manual deploy works, automate it with the workflow under
`.github/workflows/cd.yml` (added in a later commit). It uses **OIDC**
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
$AppName  = "academiaauditiva-cd"
$Repo     = "lcarli/AcademiaAuditiva"
$AppId    = az ad app create --display-name $AppName --query appId -o tsv
$ObjectId = az ad app show --id $AppId --query id -o tsv
$SpId     = az ad sp create --id $AppId --query id -o tsv

# Federated credential for pushes to main
az ad app federated-credential create --id $AppId --parameters @"
{
  "name": "main-branch",
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
| Log Analytics + AppInsights | First 5 GB free | $0 |
| **Total** | | **~$28** |
