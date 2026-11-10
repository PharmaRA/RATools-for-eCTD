param([Parameter(Mandatory)][string]$Evidence, [Parameter(Mandatory)][string]$QualifiedGuard, [Parameter(Mandatory)][string]$Filter, [string]$BuildFrom)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$run = [IO.Path]::GetFullPath($Evidence)
if (-not $run.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence outside local-check' }
for ($path=$run;$path;$path=[IO.Path]::GetDirectoryName($path)) { if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked run path refused' } }
$Label = Split-Path $run -Leaf
if (Test-Path -LiteralPath $run) { throw 'Fresh run required' }
[IO.Directory]::CreateDirectory($run) | Out-Null
$guardRoot = Join-Path $repo 'scripts/local-check/native-no-delete'
$qualifiedRoot = [IO.Path]::GetFullPath($QualifiedGuard)
$qualified = Get-Content -LiteralPath (Join-Path $qualifiedRoot 'build.json') -Raw | ConvertFrom-Json
$proof = Get-Content -LiteralPath (Join-Path $qualifiedRoot 'verification.json') -Raw | ConvertFrom-Json
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
if (-not $proof.passed -or (Sha (Join-Path $guardRoot 'NativeNoDelete.cs')) -ne $qualified.guardSourceSha256 -or
    (Sha (Join-Path $guardRoot 'RetainedMovePolicy.cs')) -ne $qualified.retainedMovePolicySha256 -or
    (Sha (Join-Path $env:SystemRoot 'System32/ntdll.dll')) -ne $qualified.ntdllSha256) { throw 'Guard qualification differs' }
$qualificationInputs=Get-Content -LiteralPath (Join-Path $qualifiedRoot 'qualification-inputs.json') -Raw|ConvertFrom-Json -AsHashtable
if ((Sha (Join-Path $qualifiedRoot 'qualification-inputs.json')) -ne $proof.qualificationInputsSha256 -or $qualificationInputs.Count -ne 8) { throw 'Qualification inputs not bound' }
foreach($entry in $qualificationInputs.GetEnumerator()){if((Sha (Join-Path $guardRoot $entry.Key)) -ne $entry.Value){throw 'Qualification source changed'}}
$oldRetentionRoot=$env:RATOOLS_TEST_RETENTION_ROOT
Add-Type -Path @((Join-Path $guardRoot 'NativeNoDelete.cs'), (Join-Path $guardRoot 'GuardPolicyTests.cs'), (Join-Path $guardRoot 'RetainedMovePolicy.cs'), (Join-Path $guardRoot 'RetainedMoveTests.cs'))
if ([GuardPolicyTests]::Run() -ne 25) { throw 'Guard ABI preflight failed' }
function Check-Process([string]$path) {
    $receipt = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($receipt.failure -or -not $receipt.assignedBeforeResume -or -not $receipt.processTreeStopped -or $receipt.guardedProcesses -ne $receipt.totalJobProcesses) { throw 'Guard process receipt incomplete' }
    return $receipt
}
$summary = [ordered]@{label=$Label;qualifiedGuard=$qualifiedRoot;qualificationSha256=(Sha (Join-Path $qualifiedRoot 'verification.json'));passed=$false;filter=$Filter;guardSourceSha256=$qualified.guardSourceSha256;ntdllSha256=$qualified.ntdllSha256;runnerSha256=(Sha $PSCommandPath)}
try {
    $build = if ($BuildFrom) { [IO.Path]::GetFullPath($BuildFrom) } else { Join-Path $run 'build' }
    if (-not $BuildFrom) {
        [IO.Directory]::CreateDirectory($build) | Out-Null
        $entry = Join-Path $repo 'scripts/local-check/msbuild.ps1'
        $buildCode = [NativeNoDelete]::Run((Get-Command pwsh).Source, @('-NoProfile','-File',$entry,'-Evidence',$build), $repo,
            (Join-Path $run 'build.log'), (Join-Path $run 'build.process.json'), 600, 'none', $build)
        $summary.buildExit = $buildCode
        $null = Check-Process (Join-Path $run 'build.process.json')
        if ($buildCode -ne 0) { throw 'Build failed; retained build.log and build/summary.json' }
    }
    $buildSummary = Get-Content -LiteralPath (Join-Path $build 'summary.json') -Raw | ConvertFrom-Json
    if ($buildSummary.exitCode -ne 0) { throw 'Build summary failed' }
    $env:RATOOLS_TEST_REPOSITORY_ROOT = $repo
    $env:ASPNETCORE_TEST_CONTENTROOT_RATOOLS_API = Join-Path $repo 'src/RATools.Api'
    $env:DOTNET_ROOT = Join-Path $repo '.artifacts/tools/dotnet-8.0.423'
    $env:DOTNET_CLI_HOME = Join-Path $build 'cli'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $temp = Join-Path $run 'temp'; [IO.Directory]::CreateDirectory($temp) | Out-Null
    $env:TEMP = $temp; $env:TMP = $temp
    $results = Join-Path $run 'results'; [IO.Directory]::CreateDirectory($results) | Out-Null
    $assembly = Join-Path $build 'bin/RATools.Tests/Release/net8.0/RATools.Tests.dll'
    $summary.testAssemblySha256 = Sha $assembly
    $summary.sourceManifestSha256 = Sha (Join-Path $build 'source-manifest.json')
    $summary.build = $build
    $fixtureRoot = Join-Path $run 'fixtures'; [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $env:RATOOLS_TEST_RETENTION_ROOT = $fixtureRoot
    $code = [NativeNoDelete]::Run((Join-Path $env:DOTNET_ROOT 'dotnet.exe'), @('vstest',$assembly,('/TestCaseFilter:'+$Filter),'/Logger:trx;LogFileName=selected.trx',('/ResultsDirectory:'+$results)),
        $repo, (Join-Path $run 'tests.log'), (Join-Path $run 'tests.process.json'), 600, 'none', $fixtureRoot)
    $summary.testExit = $code
    $null = Check-Process (Join-Path $run 'tests.process.json')
    $trxPath = Join-Path $results 'selected.trx'
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    $skipped = @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
    $summary.tests = [ordered]@{total=[int]$counts.total;passed=[int]$counts.passed;failed=[int]$counts.failed;notExecuted=$skipped}
    $summary.resultFileSha256 = Sha $trxPath
    $summary.passed = ($code -eq 0 -and $summary.tests.total -gt 0 -and $summary.tests.passed -eq $summary.tests.total -and $summary.tests.failed -eq 0 -and $summary.tests.notExecuted -eq 0)
} catch { $summary.error = $_.Exception.Message; throw }
finally { if ($null -eq $oldRetentionRoot) { [Environment]::SetEnvironmentVariable('RATOOLS_TEST_RETENTION_ROOT', [NullString]::Value) } else { $env:RATOOLS_TEST_RETENTION_ROOT=$oldRetentionRoot }; $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'summary.json') }
$summary.tests | ConvertTo-Json
