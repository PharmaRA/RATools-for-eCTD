param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$frontendPath = Join-Path $repoRoot 'frontend'
$backendProjectPath = Join-Path $repoRoot 'src\RATools.Api\RATools.Api.csproj'
$frontendUrl = 'http://localhost:3000'
$backendUrl = 'http://localhost:5000'

if (-not (Test-Path $frontendPath)) {
    throw "Frontend directory was not found: $frontendPath"
}

if (-not (Test-Path $backendProjectPath)) {
    throw "Backend project was not found: $backendProjectPath"
}

$backendCommand = "Set-Location '$repoRoot'; dotnet run --project '$backendProjectPath' --no-launch-profile --urls $backendUrl"
$frontendCommand = "Set-Location '$frontendPath'; npm run dev"

Write-Host "Repo root: $repoRoot"
Write-Host "Backend:   $backendCommand"
Write-Host "Frontend:  $frontendCommand"
Write-Host "Browser:   $frontendUrl"

if ($DryRun) {
    Write-Host 'Dry run only. No processes started.'
    exit 0
}

Start-Process powershell -WorkingDirectory $repoRoot -ArgumentList @(
    '-NoExit',
    '-ExecutionPolicy', 'Bypass',
    '-Command', $backendCommand
)

Start-Process powershell -WorkingDirectory $frontendPath -ArgumentList @(
    '-NoExit',
    '-ExecutionPolicy', 'Bypass',
    '-Command', $frontendCommand
)

Start-Sleep -Seconds 3
Start-Process $frontendUrl

Write-Host 'Frontend, backend, and browser launch requested.'
