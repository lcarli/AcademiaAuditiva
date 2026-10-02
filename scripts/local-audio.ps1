<#
.SYNOPSIS
    Serves the piano samples locally through Azurite so the exercises play
    audio during local development.

.DESCRIPTION
    The piano samples are not in git: in Azure they live in the private
    `piano-audio` blob container (network access limited to the VNet). This
    script makes them available to a local run of the app:

      1. (optional, -DownloadFrom) signs in to a running deployment with an
         Admin account and downloads the 84 samples (C1..B7) through
         /audio/{name} into .local/audio/piano-audio (git-ignored);
      2. starts the Azurite blob emulator in Docker (container `aa-azurite`,
         bound to 127.0.0.1:10000, restarts with Docker, data kept in the
         `aa-azurite-data` volume);
      3. creates the `piano-audio` and `piano-audio-mixed` containers and
         uploads the samples;
      4. points the app at Azurite through the user-secret
         `Storage:ConnectionString` = `UseDevelopmentStorage=true`.

    Requirements: Docker, Azure CLI (`az`) and the .NET SDK. Re-running the
    script is safe: existing downloads are skipped (unless -Force) and the
    upload overwrites blobs with the same name.

.PARAMETER DownloadFrom
    Base URL of a running deployment to download the samples from, e.g.
    https://academiaauditiva.com. Omit it when the samples are already in
    -SourceDir.

.PARAMETER Credential
    Admin account used to sign in to -DownloadFrom (/audio/{name} is
    admin-only). Prompted for when omitted. Accounts with two-factor
    authentication are not supported.

.PARAMETER SourceDir
    Folder holding the .mp3 samples. Defaults to .local/audio/piano-audio at
    the repository root.

.PARAMETER Force
    Download the samples again even if they already exist locally.

.EXAMPLE
    ./scripts/local-audio.ps1 -DownloadFrom https://academiaauditiva.com

    First run: downloads the samples, starts Azurite and configures the app.

.EXAMPLE
    ./scripts/local-audio.ps1

    Later runs (e.g. after `docker rm aa-azurite`): re-uploads the local samples.
#>
[CmdletBinding()]
param(
    [string]$DownloadFrom,
    [pscredential]$Credential,
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\.local\audio\piano-audio'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path $repoRoot 'AcademiaAuditiva'
$SourceDir = [IO.Path]::GetFullPath($SourceDir)
$connectionString = 'UseDevelopmentStorage=true'
$azuriteName = 'aa-azurite'

# Same naming as ExercisePlaybackPlanner.NoteToBlob: sharps use "s" (Cs4.mp3), no flats.
$notes = 'C', 'Cs', 'D', 'Ds', 'E', 'F', 'Fs', 'G', 'Gs', 'A', 'As', 'B'
$sampleNames = foreach ($octave in 1..7) { foreach ($note in $notes) { "$note$octave.mp3" } }

function Assert-Command([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found. $Hint"
    }
}

function Invoke-Native([string]$What, [scriptblock]$Command) {
    $output = & $Command 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit code $LASTEXITCODE):`n$($output -join "`n")"
    }
}

function Get-SignedInSession([string]$BaseUrl, [pscredential]$Credential) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginUrl = "$BaseUrl/Identity/Account/Login"
    $page = Invoke-WebRequest -Uri $loginUrl -WebSession $session -UseBasicParsing
    $token = [regex]::Match($page.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    if (-not $token) {
        throw "Could not find the anti-forgery token on $loginUrl."
    }

    $form = @{
        'Input.Email'                = $Credential.UserName
        'Input.Password'             = $Credential.GetNetworkCredential().Password
        'Input.RememberMe'           = 'false'
        '__RequestVerificationToken' = $token
    }
    $null = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $form -WebSession $session -UseBasicParsing

    $authCookie = $session.Cookies.GetCookies([Uri]$BaseUrl) | Where-Object Name -eq '.AspNetCore.Identity.Application'
    if (-not $authCookie) {
        throw "Sign-in to $BaseUrl failed. Check the e-mail and password (2FA accounts are not supported)."
    }
    return $session
}

# 1. Download the samples (optional).
if ($DownloadFrom) {
    $baseUrl = $DownloadFrom.TrimEnd('/')
    if (-not $Credential) {
        $Credential = Get-Credential -Message "Sign in to $baseUrl with an Admin account (only admins can download the samples)"
    }
    New-Item -ItemType Directory -Path $SourceDir -Force | Out-Null

    Write-Host "Signing in to $baseUrl..." -ForegroundColor Cyan
    $session = Get-SignedInSession -BaseUrl $baseUrl -Credential $Credential

    $downloaded = 0
    foreach ($name in $sampleNames) {
        $target = Join-Path $SourceDir $name
        if ((Test-Path $target) -and -not $Force) { continue }

        $temp = "$target.part"
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/audio/$name" -WebSession $session -OutFile $temp -PassThru -UseBasicParsing
        }
        catch {
            Remove-Item $temp -ErrorAction SilentlyContinue
            $status = $_.Exception.Response.StatusCode
            if ($status -and [int]$status -eq 403) {
                throw "$($Credential.UserName) is not an Admin on $baseUrl; only admins can download the samples."
            }
            if ($status -and [int]$status -eq 401) {
                throw "The session on $baseUrl is no longer signed in."
            }
            throw
        }
        $contentType = "$($response.Headers['Content-Type'])"
        if ($contentType -notlike 'audio/*') {
            Remove-Item $temp -ErrorAction SilentlyContinue
            throw "Unexpected response for $name (Content-Type '$contentType'). Is the account an Admin, and is the session still signed in?"
        }
        Move-Item $temp $target -Force
        $downloaded++
    }
    Write-Host "Downloaded $downloaded sample(s) to $SourceDir." -ForegroundColor Green
}

$missing = @($sampleNames | Where-Object { -not (Test-Path (Join-Path $SourceDir $_)) })
if ($missing.Count -eq $sampleNames.Count) {
    throw "No samples found in $SourceDir. Run again with -DownloadFrom https://academiaauditiva.com (or -SourceDir <folder>)."
}
if ($missing.Count -gt 0) {
    Write-Warning "$($missing.Count) sample(s) missing in ${SourceDir}: $($missing -join ', ')"
}

# 2. Start Azurite (blob service only).
Assert-Command docker 'Install Docker Desktop and make sure it is running.'
Assert-Command az 'Install the Azure CLI: https://learn.microsoft.com/cli/azure/install-azure-cli'
Assert-Command dotnet 'Install the .NET SDK (see global.json).'

$state = docker ps -a --filter "name=^/$azuriteName$" --format '{{.State}}'
if (-not $state) {
    Write-Host "Starting Azurite ($azuriteName)..." -ForegroundColor Cyan
    Invoke-Native 'docker run' {
        docker run -d --name $azuriteName --restart unless-stopped -p 127.0.0.1:10000:10000 -v aa-azurite-data:/data `
            mcr.microsoft.com/azure-storage/azurite `
            azurite-blob --blobHost 0.0.0.0 --blobPort 10000 --location /data --loose --skipApiVersionCheck
    }
}
elseif ($state -ne 'running') {
    Write-Host "Starting existing container $azuriteName..." -ForegroundColor Cyan
    Invoke-Native 'docker start' { docker start $azuriteName }
}

$ready = $false
foreach ($attempt in 1..30) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        if ($client.ConnectAsync('127.0.0.1', 10000).Wait(1000) -and $client.Connected) { $ready = $true; break }
    }
    catch { }
    finally { $client.Dispose() }
    Start-Sleep -Seconds 1
}
if (-not $ready) {
    throw "Azurite did not start listening on 127.0.0.1:10000. Check 'docker logs $azuriteName'."
}

# 3. Create the containers and upload the samples.
Write-Host 'Uploading samples to Azurite...' -ForegroundColor Cyan
foreach ($container in 'piano-audio', 'piano-audio-mixed') {
    Invoke-Native "create container $container" {
        az storage container create --name $container --connection-string $connectionString --only-show-errors
    }
}
Invoke-Native 'upload samples' {
    az storage blob upload-batch --destination piano-audio --source $SourceDir --pattern '*.mp3' `
        --content-type 'audio/mpeg' --overwrite true --connection-string $connectionString --only-show-errors
}

# 4. Point the app at Azurite.
Invoke-Native 'dotnet user-secrets set' {
    dotnet user-secrets set 'Storage:ConnectionString' $connectionString --project $appProject
}

$count = $sampleNames.Count - $missing.Count
Write-Host "Done: $count sample(s) served by Azurite. Restart the app to pick up Storage:ConnectionString." -ForegroundColor Green
