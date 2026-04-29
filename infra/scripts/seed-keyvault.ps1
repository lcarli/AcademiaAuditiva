<#
.SYNOPSIS
    Seeds Azure Key Vault with the application secrets needed by Academia Auditiva.

.DESCRIPTION
    Idempotent: re-running updates secret values without creating new vault objects.
    The signed-in principal must hold "Key Vault Secrets Officer" on the vault.

.PARAMETER VaultName
    Name of the target Key Vault (e.g. kv-aa-prd-abcdef).

.EXAMPLE
    az login --tenant 1d70d939-06d2-4348-b658-58cb38886348
    az account set --subscription 3dc8ff32-42e4-4152-b194-46b704ed70f2
    ./infra/scripts/seed-keyvault.ps1 -VaultName kv-aa-prd-abcdef
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VaultName
)

$ErrorActionPreference = 'Stop'

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
        Write-Warning "Skipping '$Name' (empty input)."
        return
    }
    az keyvault secret set --vault-name $VaultName --name $Name --value $value --output none
    Write-Host "  ✓ $Name" -ForegroundColor Green
}

Write-Host "Seeding secrets into vault '$VaultName'..." -ForegroundColor Cyan

Write-Host "`n— Database —" -ForegroundColor Yellow
Set-Secret -Name 'ConnectionStrings--DefaultConnection' -Prompt 'SQL connection string (paste output of bicep deploy)' -Mask

Write-Host "`n— Facebook OAuth —" -ForegroundColor Yellow
Set-Secret -Name 'Facebook--AppId' -Prompt 'Facebook AppId'
Set-Secret -Name 'Facebook--AppSecret' -Prompt 'Facebook AppSecret' -Mask

Write-Host "`n— SMTP (MailKit) —" -ForegroundColor Yellow
Set-Secret -Name 'Smtp--Host' -Prompt 'SMTP host (e.g. smtp.gmail.com)'
Set-Secret -Name 'Smtp--Port' -Prompt 'SMTP port (e.g. 465)'
Set-Secret -Name 'Smtp--User' -Prompt 'SMTP user (sender email)'
Set-Secret -Name 'Smtp--Password' -Prompt 'SMTP password / app password' -Mask

Write-Host "`nDone. Restart the Container App revision so it re-reads secrets." -ForegroundColor Cyan
