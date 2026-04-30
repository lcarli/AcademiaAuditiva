#requires -Version 7.0
<#
.SYNOPSIS
  One-time setup of the GitHub Actions → Azure OIDC trust for the CD pipeline.

.DESCRIPTION
  Creates (or reuses) an Entra ID app registration + service principal,
  attaches federated credentials for `refs/heads/master` and the
  `production` deployment environment, and grants the SP RBAC on the
  resource group + ACR. Idempotent: re-running it is safe.

  After it finishes, set these three GitHub Actions secrets (printed at
  the end) on the repository:
    - AZURE_CLIENT_ID
    - AZURE_TENANT_ID
    - AZURE_SUBSCRIPTION_ID

.EXAMPLE
  ./setup-github-oidc.ps1 `
    -SubscriptionId 3dc8ff32-42e4-4152-b194-46b704ed70f2 `
    -ResourceGroup  rg-aa-prd `
    -AcrName        craaprdrmz6b3 `
    -Repo           lcarli/AcademiaAuditiva
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $AcrName,
    [Parameter(Mandatory)] [string] $Repo,
    [string] $AppName     = 'academiaauditiva-cd',
    [string] $Branch      = 'main',
    [string] $Environment = 'production'
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([Parameter(Mandatory)][string[]] $Args)
    $out = & az @Args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Args -join ' ') failed:`n$out"
    }
    return $out
}

Write-Host "==> Setting subscription context to $SubscriptionId" -ForegroundColor Cyan
Invoke-Az @('account','set','--subscription', $SubscriptionId) | Out-Null

$tenantId = (Invoke-Az @('account','show','--query','tenantId','-o','tsv')) -join ''

# 1) App registration (create or reuse)
Write-Host "==> Ensuring app registration '$AppName'" -ForegroundColor Cyan
$appId = (Invoke-Az @('ad','app','list','--display-name',$AppName,'--query','[0].appId','-o','tsv')) -join ''
if (-not $appId) {
    $appId = (Invoke-Az @('ad','app','create','--display-name',$AppName,'--query','appId','-o','tsv')) -join ''
    Write-Host "    created appId=$appId"
} else {
    Write-Host "    reusing appId=$appId"
}

# 2) Service principal (create or reuse)
Write-Host "==> Ensuring service principal" -ForegroundColor Cyan
$spObjectId = (Invoke-Az @('ad','sp','list','--filter',"appId eq '$appId'",'--query','[0].id','-o','tsv')) -join ''
if (-not $spObjectId) {
    $spObjectId = (Invoke-Az @('ad','sp','create','--id',$appId,'--query','id','-o','tsv')) -join ''
    Write-Host "    created spObjectId=$spObjectId"
} else {
    Write-Host "    reusing spObjectId=$spObjectId"
}

# 3) Federated credentials (one per trust subject)
$credentials = @(
    @{ name = "github-$Branch";       subject = "repo:${Repo}:ref:refs/heads/${Branch}" },
    @{ name = "github-env-$Environment"; subject = "repo:${Repo}:environment:${Environment}" }
)

$existing = (Invoke-Az @('ad','app','federated-credential','list','--id',$appId,'--query','[].name','-o','tsv')) -split "`n" |
    Where-Object { $_ }

foreach ($cred in $credentials) {
    if ($existing -contains $cred.name) {
        Write-Host "==> Federated credential '$($cred.name)' already exists" -ForegroundColor DarkGray
        continue
    }
    Write-Host "==> Creating federated credential '$($cred.name)'" -ForegroundColor Cyan
    $body = @{
        name      = $cred.name
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $cred.subject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json -Compress

    $tmp = New-TemporaryFile
    try {
        Set-Content -Path $tmp -Value $body -Encoding utf8 -NoNewline
        Invoke-Az @('ad','app','federated-credential','create','--id',$appId,'--parameters',"@$tmp") | Out-Null
    } finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

# 4) RBAC: Contributor on RG + AcrPush on ACR
$rgScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$acrScope = (Invoke-Az @('acr','show','-n',$AcrName,'-g',$ResourceGroup,'--query','id','-o','tsv')) -join ''

$assignments = @(
    @{ role = 'Contributor'; scope = $rgScope },
    @{ role = 'AcrPush';     scope = $acrScope }
)

foreach ($a in $assignments) {
    $existsLine = (Invoke-Az @(
        'role','assignment','list',
        '--assignee', $appId,
        '--scope',    $a.scope,
        '--role',     $a.role,
        '--query',    '[0].id',
        '-o','tsv'
    )) -join ''
    if ($existsLine) {
        Write-Host "==> Role '$($a.role)' on $($a.scope) already assigned" -ForegroundColor DarkGray
        continue
    }
    Write-Host "==> Granting '$($a.role)' on $($a.scope)" -ForegroundColor Cyan
    Invoke-Az @('role','assignment','create',
        '--assignee-object-id',     $spObjectId,
        '--assignee-principal-type','ServicePrincipal',
        '--role',                   $a.role,
        '--scope',                  $a.scope) | Out-Null
}

Write-Host ""
Write-Host "==================== DONE ====================" -ForegroundColor Green
Write-Host "Set the following GitHub Actions secrets on ${Repo}:" -ForegroundColor Green
Write-Host "  AZURE_CLIENT_ID       = $appId"
Write-Host "  AZURE_TENANT_ID       = $tenantId"
Write-Host "  AZURE_SUBSCRIPTION_ID = $SubscriptionId"
Write-Host ""
Write-Host "Also set the GitHub repository variable:" -ForegroundColor Green
Write-Host "  ACR_NAME              = $AcrName"
Write-Host ""
Write-Host "And create a deployment environment named '$Environment'" -ForegroundColor Green
Write-Host "(Settings → Environments) so the second federated credential takes effect."
Write-Host "=============================================="
