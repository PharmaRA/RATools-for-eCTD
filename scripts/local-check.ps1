[CmdletBinding()]
param([ValidateSet('build','msbuild','msbuild-tests','msbuild-contracts','msbuild-import','msbuild-nodes','msbuild-package','msbuild-publishing','msbuild-storage','msbuild-workspace','msbuild-api','msbuild-pruner','msbuild-fixtures','msbuild-config','msbuild-documents','msbuild-links','pdf','capture','upload','multipart','report','validation-ui','frontend-all','frontend-static','input-api','input-browser')][string]$Task = 'build')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$run = (Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo ".artifacts/local-check/$run"
$null = New-Item -ItemType Directory -Path $evidence
Write-Host "Local $Task evidence: $evidence"
if ($Task -in @('msbuild','msbuild-tests','msbuild-contracts','msbuild-import','msbuild-nodes','msbuild-package','msbuild-publishing','msbuild-storage','msbuild-workspace','msbuild-api','msbuild-pruner','msbuild-fixtures','msbuild-config','msbuild-documents','msbuild-links')) {
    $testGroup = if($Task -eq 'msbuild-links'){'links'}elseif($Task -eq 'msbuild-documents'){'documents'}elseif($Task -eq 'msbuild-config'){'config'}elseif($Task -eq 'msbuild-fixtures'){'fixtures'}elseif($Task -eq 'msbuild-pruner'){'pruner'}elseif($Task -eq 'msbuild-api'){'api'}elseif($Task -eq 'msbuild-workspace'){'workspace'}elseif($Task -eq 'msbuild-storage'){'storage'}elseif($Task -eq 'msbuild-publishing'){'publishing'}elseif($Task -eq 'msbuild-package'){'package'}elseif($Task -eq 'msbuild-nodes'){'nodes'}elseif($Task -eq 'msbuild-import'){'import'}elseif($Task -eq 'msbuild-contracts'){'contracts'}else{'core'}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'local-check/msbuild.ps1') -Evidence $evidence -RunSelectedTests:($Task -ne 'msbuild') -TestGroup $testGroup
    exit $LASTEXITCODE
} elseif ($Task -in @('frontend-all','frontend-static')) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'local-check/frontend-all.ps1') -Evidence $evidence -StaticOnly:($Task -eq 'frontend-static')
    exit $LASTEXITCODE
} elseif ($Task -eq 'validation-ui') {
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
