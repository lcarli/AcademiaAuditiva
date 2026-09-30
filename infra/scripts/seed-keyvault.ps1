#requires -Version 7.0
<#
.SYNOPSIS
    Sets the optional Academia Auditiva secrets in Key Vault.

.DESCRIPTION
    The vault has public network access disabled, so its data plane
    (az keyvault secret set, the portal's Secrets blade) only works from
    inside the VNet. This script writes the secrets through Azure Resource
    Manager instead, which needs Contributor or Key Vault Contributor on the
    vault rather than a data-plane role.

    Leave a prompt empty to keep the current value. Every value written is
    enabled, so this also turns back on a secret that was disabled to switch
    a feature off (the app skips disabled secrets). The app reads Key Vault
    only at startup, so restart the revision afterwards.

    ConnectionStrings--DefaultConnection is written by the Bicep deployment.

.EXAMPLE
    az login --tenant 1d70d939-06d2-4348-b658-58cb38886348
    ./infra/scripts/seed-keyvault.ps1 -VaultName kv-aa-prd-rmz6b3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VaultName,
    [string] $SubscriptionId = '3dc8ff32-42e4-4152-b194-46b704ed70f2'
)

$ErrorActionPreference = 'Stop'

$vaultId = az keyvault show --name $VaultName --subscription $SubscriptionId --query id -o tsv
if ($LASTEXITCODE -ne 0 -or -not $vaultId) { throw "Key Vault '$VaultName' not found in subscription $SubscriptionId." }

function Set-Secret {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Prompt,
        [switch] $Mask
    )
    if ($Mask) {
        $secure = Read-Host -Prompt $Prompt -AsSecureString
        $value = [System.Net.NetworkCredential]::new('', $secure).Password
    } else {
        $value = Read-Host -Prompt $Prompt
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        Write-Host "  - $Name unchanged" -ForegroundColor DarkGray
        return
    }
    # A file keeps the value off the command line, where other processes can read it.
    $body = New-TemporaryFile
    try {
        $payload = @{ properties = @{ value = $value; attributes = @{ enabled = $true } } }
        Set-Content -Path $body -Value ($payload | ConvertTo-Json -Compress) -Encoding utf8NoBOM -NoNewline
        az rest --method put --url "https://management.azure.com$vaultId/secrets/${Name}?api-version=2023-07-01" --body "@$body" -o none
        if ($LASTEXITCODE -ne 0) { throw "Failed to set '$Name'." }
    } finally {
        Remove-Item $body -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  ✓ $Name" -ForegroundColor Green
}

Write-Host "Setting secrets in vault '$VaultName' (empty input keeps the current value)..." -ForegroundColor Cyan

Write-Host "`n— Facebook OAuth —" -ForegroundColor Yellow
Set-Secret -Name 'Facebook--AppId' -Prompt 'Facebook AppId'
Set-Secret -Name 'Facebook--AppSecret' -Prompt 'Facebook AppSecret' -Mask

Write-Host "`n— SMTP (MailKit) —" -ForegroundColor Yellow
Set-Secret -Name 'Smtp--Host' -Prompt 'SMTP host (e.g. smtp.gmail.com)'
Set-Secret -Name 'Smtp--Port' -Prompt 'SMTP port (e.g. 465)'
Set-Secret -Name 'Smtp--User' -Prompt 'SMTP user (sender email)'
Set-Secret -Name 'Smtp--Password' -Prompt 'SMTP password / app password' -Mask

Write-Host "`n— Bootstrap admin —" -ForegroundColor Yellow
Write-Host "  Only used to create the Admin__Email account if it doesn't exist yet." -ForegroundColor DarkGray
Set-Secret -Name 'Admin--InitialPassword' -Prompt 'Initial admin password' -Mask

Write-Host "`nDone. Restart the Container App revision so it re-reads the secrets." -ForegroundColor Cyan
