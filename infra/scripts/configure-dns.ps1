#requires -Version 7.0
<#
.SYNOPSIS
    Points the custom domains of the container app at its environment.

.DESCRIPTION
    For each custom domain in the parameters file, writes the records that
    Container Apps needs to validate and serve it into the domain's Azure DNS
    zone. The zone can be in another subscription or tenant.
      - apex domain: @ A -> environment static IP, asuid TXT -> verification id
      - subdomain:   <sub> CNAME -> app FQDN, asuid.<sub> TXT -> verification id
    Each record set is replaced as a whole, so re-running is safe. Other
    records in the zone are left alone.

    If the zone is in another tenant, sign in to both first:
    az login --tenant <app tenant>; az login --tenant <DNS tenant>.

    Run it after deploy-infra.ps1 -SkipCustomDomains, then run
    deploy-infra.ps1 again to issue and bind the certificates.

.EXAMPLE
    ./infra/scripts/configure-dns.ps1 -DnsSubscriptionId <id> -DnsResourceGroup <rg> -WhatIf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DnsSubscriptionId,
    [Parameter(Mandatory)] [string] $DnsResourceGroup,
    [string] $ZoneName = 'academiaauditiva.com',
    [string] $ParametersFile = (Join-Path $PSScriptRoot '..' 'main.parameters.prd.json'),
    [string] $SubscriptionId = '3dc8ff32-42e4-4152-b194-46b704ed70f2',
    [int] $Ttl = 300,
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'

# Only stdout is returned; stderr (progress, warnings) is shown if az fails.
function Invoke-Az {
    $stderr = New-TemporaryFile
    try {
        $out = & az @args 2>$stderr
        if ($LASTEXITCODE -ne 0) {
            $message = (Get-Content -Raw $stderr) -split '[\r\n]+' | Where-Object { $_ -and $_ -notmatch 'Running \.\.$' }
            throw "az $($args -join ' ') failed (exit code $LASTEXITCODE):`n$($message -join "`n")"
        }
        return $out
    } finally {
        Remove-Item $stderr -Force -ErrorAction SilentlyContinue
    }
}

function Format-RecordSet($properties) {
    if (-not $properties) { return '(none)' }
    $value = if ($properties.ARecords) { $properties.ARecords.ipv4Address -join ', ' }
        elseif ($properties.CNAMERecord) { $properties.CNAMERecord.cname }
        elseif ($properties.TXTRecords) { ($properties.TXTRecords | ForEach-Object { $_.value -join '' }) -join ', ' }
        else { '(no records)' }
    if ($properties.TTL) { "$value (TTL $($properties.TTL))" } else { $value }
}

$parameters = (Get-Content -Raw $ParametersFile | ConvertFrom-Json).parameters
function Get-Parameter([string] $Name, $Default) {
    if ($parameters.PSObject.Properties[$Name]) { $parameters.$Name.value } else { $Default }
}

$prefix  = Get-Parameter 'resourcePrefix' 'aa'
$envName = Get-Parameter 'envName' 'prd'
$domains = @(Get-Parameter 'customDomains' @())
if ($domains.Count -eq 0) { throw "No customDomains in $ParametersFile." }

# Same naming as resources.bicep.
$resourceGroup = "rg-$prefix-$envName"
$environment = Invoke-Az containerapp env show -g $resourceGroup -n "cae-$prefix-$envName" --subscription $SubscriptionId `
    --query '{ip: properties.staticIp, verificationId: properties.customDomainConfiguration.customDomainVerificationId}' -o json |
    ConvertFrom-Json
$fqdn = Invoke-Az containerapp show -g $resourceGroup -n "ca-$prefix-$envName" --subscription $SubscriptionId `
    --query properties.configuration.ingress.fqdn -o tsv
if (-not $environment.ip -or -not $environment.verificationId -or -not $fqdn) {
    throw "Could not read the environment IP, verification id or app FQDN. Deploy with deploy-infra.ps1 first."
}

$verification = @{ TXTRecords = @(@{ value = @($environment.verificationId) }) }
$records = [ordered]@{}
foreach ($domain in $domains) {
    $hostName = $domain.name.ToLowerInvariant()
    if ($hostName -eq $ZoneName) {
        $records['A/@'] = @{ ARecords = @(@{ ipv4Address = $environment.ip }) }
        $records['TXT/asuid'] = $verification
    } elseif ($hostName.EndsWith(".$ZoneName")) {
        $relativeName = $hostName.Substring(0, $hostName.Length - $ZoneName.Length - 1)
        $records["CNAME/$relativeName"] = @{ CNAMERecord = @{ cname = $fqdn } }
        $records["TXT/asuid.$relativeName"] = $verification
    } else {
        throw "$hostName is not in zone $ZoneName."
    }
}

$zoneUrl = "https://management.azure.com/subscriptions/$DnsSubscriptionId/resourceGroups/$DnsResourceGroup/providers/Microsoft.Network/dnsZones/$ZoneName"
$body = New-TemporaryFile
try {
    foreach ($key in $records.Keys) {
        $type, $name = $key -split '/', 2
        $url = "$zoneUrl/$type/${name}?api-version=2018-05-01"
        $properties = @{ TTL = $Ttl } + $records[$key]

        # A missing record set is a 404, not an error here.
        $current = az rest --method get --url $url --query properties -o json 2>$null
        $current = if ($LASTEXITCODE -eq 0) { $current | ConvertFrom-Json } else { $null }

        Write-Host ("{0,-5} {1,-12} {2}  ->  {3}" -f $type, $name, (Format-RecordSet $current), (Format-RecordSet $properties))
        if ($WhatIf) { continue }

        # A file avoids the quoting rules for JSON arguments to az.cmd on Windows.
        Set-Content -Path $body -Value (@{ properties = $properties } | ConvertTo-Json -Depth 5 -Compress) -Encoding utf8NoBOM -NoNewline
        Invoke-Az rest --method put --url $url --body "@$body" -o none | Out-Null
    }
} finally {
    Remove-Item $body -Force -ErrorAction SilentlyContinue
}

if ($WhatIf) { Write-Host "`nWhat-if only: nothing was changed." }
else { Write-Host "`nDNS updated. Once it resolves, run deploy-infra.ps1 to issue and bind the certificates." }
