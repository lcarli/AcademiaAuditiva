<#
.SYNOPSIS
    Grants the Container App's user-assigned Managed Identity access to the
    Azure SQL database as a contained AAD user.

.DESCRIPTION
    Must be executed by a principal that is the AAD admin of the SQL server
    (or a member of the configured AAD admin group).
    Run once after the first `az deployment sub create`.

    Requires:
      - PowerShell 7+
      - SqlServer module: Install-Module SqlServer
      - Active `az login` to the tenant that owns the SQL server

.PARAMETER ServerFqdn
    e.g. sql-aa-prd-abcdef.database.windows.net

.PARAMETER DatabaseName
    e.g. sqldb-aa-prd

.PARAMETER ManagedIdentityName
    e.g. id-aa-prd

.EXAMPLE
    ./infra/scripts/grant-mi-sql.ps1 `
        -ServerFqdn sql-aa-prd-abcdef.database.windows.net `
        -DatabaseName sqldb-aa-prd `
        -ManagedIdentityName id-aa-prd
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ServerFqdn,
    [Parameter(Mandatory)] [string] $DatabaseName,
    [Parameter(Mandatory)] [string] $ManagedIdentityName
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Write-Host "Installing SqlServer module..." -ForegroundColor Yellow
    Install-Module -Name SqlServer -Scope CurrentUser -Force -AllowClobber
}

Write-Host "Acquiring AAD access token for SQL..." -ForegroundColor Cyan
$accessToken = az account get-access-token --resource https://database.windows.net --query accessToken -o tsv
if (-not $accessToken) { throw "Failed to acquire AAD token. Run 'az login' first." }

$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$ManagedIdentityName')
BEGIN
    CREATE USER [$ManagedIdentityName] FROM EXTERNAL PROVIDER;
END
ALTER ROLE db_datareader ADD MEMBER [$ManagedIdentityName];
ALTER ROLE db_datawriter ADD MEMBER [$ManagedIdentityName];
ALTER ROLE db_ddladmin   ADD MEMBER [$ManagedIdentityName];
"@

Write-Host "Granting roles to managed identity '$ManagedIdentityName' on db '$DatabaseName'..." -ForegroundColor Cyan
Invoke-Sqlcmd `
    -ServerInstance $ServerFqdn `
    -Database $DatabaseName `
    -AccessToken $accessToken `
    -Query $sql `
    -ErrorAction Stop

Write-Host "`n  ✓ Managed Identity provisioned in SQL." -ForegroundColor Green
