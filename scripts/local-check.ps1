[CmdletBinding()]
param([ValidateSet('build','pdf','capture','validation-ui','input-api')][string]$Task = 'build')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$run = (Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo ".artifacts/local-check/$run"
$null = New-Item -ItemType Directory -Path $evidence
Write-Host "Local $Task evidence: $evidence"
if ($Task -eq 'validation-ui') {
    $guard = [Uri]::new((Join-Path $repo 'scripts/remote-check/no-delete.mjs')).AbsoluteUri
    & node --import $guard (Join-Path $repo 'scripts/local-check/frontend.mjs') $evidence 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'frontend.log')
    $result = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath (Join-Path $evidence 'summary.json'))) {
        @{ task = $Task; exitCode = $result; category = 'runner-startup-failed' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'summary.json')
    }
    exit $result
} else {
    & python (Join-Path $repo 'scripts/local-check/build.py') $evidence $Task
}
exit $LASTEXITCODE
