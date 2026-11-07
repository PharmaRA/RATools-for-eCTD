[CmdletBinding()]
param([ValidateSet('build','pdf','capture')][string]$Task = 'build')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$run = (Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo ".artifacts/local-check/$run"
$null = New-Item -ItemType Directory -Path $evidence
Write-Host "Local $Task evidence: $evidence"
& python (Join-Path $repo 'scripts/local-check/build.py') $evidence $Task
exit $LASTEXITCODE
