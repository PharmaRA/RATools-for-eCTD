param(
    [switch]$DryRun,
    [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$frontendPath = Join-Path $repoRoot 'frontend'
$backendProjectPath = Join-Path $repoRoot 'src\RATools.Api\RATools.Api.csproj'
$frontendUrl = 'http://localhost:3000'
$backendUrl = 'http://localhost:5000'
$startupTimeoutSeconds = 60

function Test-HttpEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Get-ListeningProcessIds {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    return @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
}

function Wait-ForEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpEndpoint -Url $Url) {
            Write-Host "$Name is ready at $Url"
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "$Name did not become ready within $TimeoutSeconds seconds: $Url"
}

function Start-Backend {
    $backendCommand = "Set-Location -LiteralPath '$repoRoot'; & dotnet run --project '$backendProjectPath' --no-launch-profile -- --urls $backendUrl"

    Write-Host "Backend:   $backendCommand"

    Start-Process powershell -WorkingDirectory $repoRoot -ArgumentList @(
        '-NoExit',
        '-ExecutionPolicy', 'Bypass',
        '-Command', $backendCommand
    ) | Out-Null

    Wait-ForEndpoint -Url "$backendUrl/health" -Name 'Backend API' -TimeoutSeconds $startupTimeoutSeconds
}

function Start-Frontend {
    $frontendCommand = "Set-Location -LiteralPath '$frontendPath'; & npm.cmd run dev -- --host localhost --port 3000 --strictPort"

    Write-Host "Frontend:  $frontendCommand"

    Start-Process powershell -WorkingDirectory $frontendPath -ArgumentList @(
        '-NoExit',
        '-ExecutionPolicy', 'Bypass',
        '-Command', $frontendCommand
    ) | Out-Null

    Wait-ForEndpoint -Url $frontendUrl -Name 'Frontend dev server' -TimeoutSeconds $startupTimeoutSeconds
}

if (-not (Test-Path $frontendPath)) {
    throw "Frontend directory was not found: $frontendPath"
}

if (-not (Test-Path $backendProjectPath)) {
    throw "Backend project was not found: $backendProjectPath"
}

Write-Host "Repo root: $repoRoot"
Write-Host "Browser:   $frontendUrl"

if ($DryRun) {
    Write-Host "Backend URL: $backendUrl"
    Write-Host "Frontend URL: $frontendUrl"
    Write-Host 'Dry run only. No processes started.'
    exit 0
}

$backendListenerPids = Get-ListeningProcessIds -Port 5000
if (Test-HttpEndpoint -Url "$backendUrl/health") {
    Write-Host "Backend API already available at $backendUrl. Reusing existing process."
}
elseif ($backendListenerPids.Count -gt 0) {
    throw "Port 5000 is already in use by process id(s): $($backendListenerPids -join ', '). Stop that process or free the port, then retry."
}
else {
    Start-Backend
}

$frontendListenerPids = Get-ListeningProcessIds -Port 3000
if (Test-HttpEndpoint -Url $frontendUrl) {
    Write-Host "Frontend dev server already available at $frontendUrl. Reusing existing process."
}
elseif ($frontendListenerPids.Count -gt 0) {
    throw "Port 3000 is already in use by process id(s): $($frontendListenerPids -join ', '). Stop that process or free the port, then retry."
}
else {
    Start-Frontend
}

if (-not $SkipBrowser) {
    Start-Process $frontendUrl | Out-Null
}

Write-Host 'Frontend and backend are ready.'
