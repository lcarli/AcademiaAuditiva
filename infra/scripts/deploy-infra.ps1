#requires -Version 7.0
<#
.SYNOPSIS
    Deploys infra/main.bicep without undoing what CD and earlier deploys set up.

.DESCRIPTION
    A plain `az deployment sub create` resets the container image to the
    placeholder in main.bicep and can't tell whether the custom domain
    certificates exist. This wrapper:
      - passes the image the container app runs now (or -Image),
      - tells Bicep whether the managed certificates of customDomains are
        already issued, so redeploys bind them instead of issuing them again,
      - prints what DNS needs: environment IP, verification id, app FQDN.

    A custom domain can only be added once its DNS records point at the
    environment. On a new environment, deploy with -SkipCustomDomains, run
    configure-dns.ps1, then deploy again without it.

.EXAMPLE
    ./infra/scripts/deploy-infra.ps1 -WhatIf

.EXAMPLE
    ./infra/scripts/deploy-infra.ps1 -SkipCustomDomains -Image craaprdrmz6b3.azurecr.io/academiaauditiva:<sha>
#>
[CmdletBinding()]
param(
    [string] $ParametersFile = (Join-Path $PSScriptRoot '..' 'main.parameters.prd.json'),
    [string] $SubscriptionId = '3dc8ff32-42e4-4152-b194-46b704ed70f2',
    [string] $Image,
    [switch] $SkipCustomDomains,
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

$parameters = (Get-Content -Raw $ParametersFile | ConvertFrom-Json).parameters
function Get-Parameter([string] $Name, $Default) {
    if ($parameters.PSObject.Properties[$Name]) { $parameters.$Name.value } else { $Default }
}

$prefix   = Get-Parameter 'resourcePrefix' 'aa'
$envName  = Get-Parameter 'envName' 'prd'
$location = Get-Parameter 'location' 'canadacentral'
$domains  = if ($SkipCustomDomains) { @() } else { @(Get-Parameter 'customDomains' @()) }

# Same naming as resources.bicep.
$resourceGroup = "rg-$prefix-$envName"
$containerApp  = "ca-$prefix-$envName"
$environment   = "cae-$prefix-$envName"

Invoke-Az account set --subscription $SubscriptionId | Out-Null
$rgExists = (Invoke-Az group exists --name $resourceGroup) -eq 'true'

if (-not $Image -and $rgExists) {
    $Image = Invoke-Az containerapp list -g $resourceGroup --query "[?name=='$containerApp'] | [0].properties.template.containers[0].image" -o tsv
}
if ($Image) {
    Write-Host "Container image: $Image"
} else {
    Write-Warning "No running container app found: Bicep deploys its placeholder image until CD pushes one."
}

$certificatesExist = $false
if ($domains.Count -gt 0 -and $rgExists -and (Invoke-Az containerapp env list -g $resourceGroup --query "[?name=='$environment'].name" -o tsv)) {
    $issued = @(Invoke-Az containerapp env certificate list -g $resourceGroup -n $environment --managed-certificates-only `
        --query "[?properties.provisioningState=='Succeeded'].name" -o tsv)
    # Same naming as managedCertificateNames in resources.bicep.
    $expected = $domains | ForEach-Object { 'mc-' + ($_.name -replace '\.', '-') }
    $certificatesExist = -not ($expected | Where-Object { $_ -notin $issued })
}
Write-Host "Custom domains: $(if ($domains.Count) { ($domains.name -join ', ') } else { 'none' }); certificates already issued: $certificatesExist"

$deployArgs = @(
    '--location', $location,
    '--template-file', (Join-Path $PSScriptRoot '..' 'main.bicep'),
    '--parameters', $ParametersFile,
    '--parameters', "customDomainCertificatesExist=$($certificatesExist.ToString().ToLowerInvariant())"
)
if ($Image) { $deployArgs += @('--parameters', "containerImage=$Image") }
if ($SkipCustomDomains) { $deployArgs += @('--parameters', 'customDomains=[]') }

if ($WhatIf) {
    az deployment sub what-if @deployArgs
    if ($LASTEXITCODE -ne 0) { throw "what-if failed (exit code $LASTEXITCODE)." }
    return
}

$deploymentName = "aa-$envName-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Write-Host "Deploying $deploymentName (the first deploy of an environment takes 15-30 minutes)..."
$outputs = Invoke-Az deployment sub create --name $deploymentName @deployArgs --query properties.outputs -o json | ConvertFrom-Json

Write-Host ""
Write-Host "App FQDN:               $($outputs.containerAppFqdn.value)"
Write-Host "Environment static IP:  $($outputs.containerAppEnvironmentStaticIp.value)"
Write-Host "Domain verification id: $($outputs.customDomainVerificationId.value)"
Write-Host ""
Write-Host "DNS (configure-dns.ps1): @ A -> static IP, <sub> CNAME -> app FQDN, asuid[.<sub>] TXT -> verification id."
