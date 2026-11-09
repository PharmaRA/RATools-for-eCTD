[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildRun,[Parameter(Mandatory)][string]$GuardRun,[Parameter(Mandatory)][string]$Evidence,[ValidateSet('resources','workflow','combined')][string]$Group='resources',[string]$BuildGuardRun)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'These tests require actual Windows execution'}
if(-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('RATOOLS_TEST_POSTGRES'))){throw 'Native-only runner requires PostgreSQL environment absent'}
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$allowed=Join-Path $repo '.artifacts/local-check/'
if(-not $BuildGuardRun){$BuildGuardRun=$GuardRun}
foreach($path in @($BuildRun,$GuardRun,$Evidence,$BuildGuardRun)){
 $full=[IO.Path]::GetFullPath($path)
 if(-not $full.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence/build path outside local-check'}
 for($p=$full;$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked evidence ancestor refused'}}
}
$BuildRun=[IO.Path]::GetFullPath($BuildRun);$GuardRun=[IO.Path]::GetFullPath($GuardRun);$Evidence=[IO.Path]::GetFullPath($Evidence);$BuildGuardRun=[IO.Path]::GetFullPath($BuildGuardRun)
if(Test-Path -LiteralPath $Evidence){throw 'Existing run directory refused'}
New-Item -ItemType Directory -Path $Evidence | Out-Null
function Sha([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Require-Process([string]$path,[int]$code){
 $p=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 if($p.exitCode -ne $code -or $p.failure -or $p.assignedBeforeResume -ne $true -or $p.processTreeStopped -ne $true -or $p.guardedProcesses -ne $p.totalJobProcesses){throw 'Guarded process evidence incomplete'}
}
$guard=Join-Path $PSScriptRoot 'NativeNoDelete.cs';$guardHash=Sha $guard
$qualified=Get-Content -LiteralPath (Join-Path $GuardRun 'build.json') -Raw | ConvertFrom-Json
$verification=Get-Content -LiteralPath (Join-Path $GuardRun 'verification.json') -Raw | ConvertFrom-Json
if($verification.passed -ne $true -or $qualified.guardSourceSha256 -ne $guardHash -or (Sha (Join-Path $env:SystemRoot 'System32/ntdll.dll')) -ne $qualified.ntdllSha256){throw 'Qualified guard identity differs'}
if((Sha (Join-Path $PSScriptRoot 'RetainedMovePolicy.cs')) -ne $qualified.retainedMovePolicySha256 -or @($verification.steps | Where-Object {$_.name -eq 'retained-move' -and $_.exitCode -eq 0}).Count -ne 1){throw 'Retained move policy is not qualified'}
$launch=Get-Content -LiteralPath (Join-Path $BuildRun 'launch.json') -Raw | ConvertFrom-Json
$buildGuard=Get-Content -LiteralPath (Join-Path $BuildGuardRun 'build.json') -Raw | ConvertFrom-Json
$buildGuardVerification=Get-Content -LiteralPath (Join-Path $BuildGuardRun 'verification.json') -Raw | ConvertFrom-Json
if($launch.exitCode -ne 0 -or $launch.guardSha256 -ne $buildGuard.guardSourceSha256 -or $buildGuardVerification.passed -ne $true -or $buildGuard.ntdllSha256 -ne $qualified.ntdllSha256){throw 'Build guard is not separately qualified and bound'}
Require-Process (Join-Path $BuildRun 'runner.process.json') 0
$build=Join-Path $BuildRun 'build'
$buildSummary=Get-Content -LiteralPath (Join-Path $build 'summary.json') -Raw | ConvertFrom-Json
$manifestPath=Join-Path $build 'source-manifest.json'
if($buildSummary.exitCode -ne 0 -or (Sha $manifestPath) -ne $buildSummary.sourceManifestSha256){throw 'Build source manifest mismatch'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
foreach($item in $manifest.GetEnumerator()){if((Sha (Join-Path $repo $item.Key)) -ne $item.Value){throw "Build input changed: $($item.Key)"}}
$assembly=Join-Path $build 'bin/RATools.Tests/Release/net8.0/RATools.Tests.dll'
$python='C:\Program Files\Python311\python.exe';$qpdf=Join-Path $repo '.artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe'
$worker=Join-Path $repo 'scripts/pdf_content_worker.py';$model=Join-Path $repo '.artifacts/p508-qualification/tessdata/eng.traineddata'
$sourceBindings=@{guardSha256=$guardHash;entrySha256=(Sha $PSCommandPath);sourceManifestSha256=(Sha $manifestPath);testAssemblySha256=(Sha $assembly);pythonSha256=(Sha $python);qpdfSha256=(Sha $qpdf);workerSha256=(Sha $worker);modelSha256=(Sha $model);ntdllSha256=$qualified.ntdllSha256}
$sourceBindings.navigationFixtureSha256=Sha (Join-Path $repo 'tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf')
$sourceBindings.buildGuardSha256=$buildGuard.guardSourceSha256
$sourceBindings.retainedMovePolicySha256=$qualified.retainedMovePolicySha256
if($sourceBindings.modelSha256 -ne '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'){throw 'OCR model differs from fixed test input'}
$sourceBindings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'bindings.json')
$outputBindings=@{};Get-ChildItem -LiteralPath (Split-Path -Parent $assembly) -File | ForEach-Object {$outputBindings[$_.Name]=Sha $_.FullName}
$outputBindings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'test-output-manifest.json')
$classPrefix='RATools.Tests.DocumentProcessing.'
$classes=@{};$methods=@()
switch($Group){
 'resources' {$classes[$classPrefix+'PdfContentResourceTests']=5;$methods=@('QpdfCpuLimitEndsTheOwnedToolAndReleasesItsBudgetSlot','QpdfHostEnforcesTheConfiguredJobMemoryLimitBeforeExecutingToolCode','PythonCapabilitiesShareTheProcessBudgetAndRecoverAfterCancelledWaiting') | ForEach-Object {$classPrefix+'QpdfConcurrencyTests.'+$_};$expected=8}
 'workflow' {$classes[$classPrefix+'PdfContentWorkflowTests']=6;$classes[$classPrefix+'PdfContentHttpTests']=1;$expected=7}
 'combined' {$classes[$classPrefix+'PdfCombinedQualificationTests']=2;$expected=2}
}
$filter=@($classes.Keys | Sort-Object | ForEach-Object {'FullyQualifiedName~'+$_+'.'})+@($methods | ForEach-Object {'FullyQualifiedName='+$_})
$filter=$filter -join '|'
$summary=@{passed=$false;group=$Group;expected=$expected;sourceBindings=$sourceBindings;steps=@();scope='Selected original Windows native tests with ntdll no-delete interception; not PostgreSQL/full-suite/Formal'}
$saved=@{};foreach($key in @('TEMP','TMP','RATOOLS_QPDF','RATOOLS_TEST_PYTHON','RATOOLS_TEST_REPOSITORY_ROOT','ASPNETCORE_TEST_CONTENTROOT_RATOOLS_API','RATOOLS_NATIVE_FIXTURE_ROOT')){$saved[$key]=[Environment]::GetEnvironmentVariable($key)}
try {
 $temp=Join-Path $Evidence 'temp';New-Item -ItemType Directory -Path $temp | Out-Null
 $fixtureRoot=Join-Path $Evidence 'f';New-Item -ItemType Directory -Path $fixtureRoot | Out-Null;$env:RATOOLS_NATIVE_FIXTURE_ROOT=$fixtureRoot
 $env:TEMP=$temp;$env:TMP=$temp;$env:RATOOLS_QPDF=$qpdf;$env:RATOOLS_TEST_PYTHON=$python;$env:RATOOLS_TEST_REPOSITORY_ROOT=$repo;$env:ASPNETCORE_TEST_CONTENTROOT_RATOOLS_API=Join-Path $repo 'src/RATools.Api'
 Add-Type -Path @($guard,(Join-Path $PSScriptRoot 'GuardPolicyTests.cs'),(Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'),(Join-Path $PSScriptRoot 'RetainedMoveTests.cs'))
 if([GuardPolicyTests]::Run() -ne 25){throw 'Policy ABI preflight failed'}
 $dotnet=Join-Path $repo '.artifacts/tools/dotnet-8.0.423/dotnet.exe'
 $discovery=Join-Path $Evidence 'discovery.log';$receipt=Join-Path $Evidence 'discovery.process.json'
 $code=[NativeNoDelete]::Run($dotnet,@('vstest',$assembly,'/ListTests'),$repo,$discovery,$receipt,120)
 Require-Process $receipt $code
 if($code -ne 0){throw 'Guarded test discovery failed'}
 $names=@(Get-Content -LiteralPath $discovery | Where-Object {$_ -match '^\s+RATools\.Tests\.'} | ForEach-Object {$_.Trim()})
 $selected=@()
 foreach($class in $classes.Keys){$chosen=@($names | Where-Object {$_.StartsWith($class+'.',[StringComparison]::Ordinal)});if($chosen.Count -ne $classes[$class]){throw "Unexpected discovery count for $class"};$selected+=$chosen}
 foreach($method in $methods){$chosen=@($names | Where-Object {$_ -ceq $method -or $_.StartsWith($method+'(',[StringComparison]::Ordinal)});if($chosen.Count -ne 1){throw "Unexpected discovery count for $method"};$selected+=$chosen}
 if($selected.Count -ne $expected){throw 'Selected discovery count mismatch'}
 $selected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'selected-names.json')
 $results=Join-Path $Evidence 'results';New-Item -ItemType Directory -Path $results | Out-Null
 $settings=Join-Path $Evidence 'selected.runsettings'
 '<RunSettings><RunConfiguration><MaxCpuCount>1</MaxCpuCount><DisableAppDomain>true</DisableAppDomain></RunConfiguration></RunSettings>' | Set-Content -LiteralPath $settings
 $log=Join-Path $Evidence 'tests.log';$receipt=Join-Path $Evidence 'tests.process.json'
 $code=[NativeNoDelete]::Run($dotnet,@('vstest',$assembly,('/Settings:'+$settings),('/TestCaseFilter:'+$filter),'/Logger:trx;LogFileName=selected.trx',('/ResultsDirectory:'+$results)),$repo,$log,$receipt,600,'none',$fixtureRoot)
 Require-Process $receipt $code
 $summary.steps+=@{name='selected-vstest';exitCode=$code}
 $trxPath=Join-Path $results 'selected.trx';[xml]$trx=Get-Content -LiteralPath $trxPath -Raw
 $c=$trx.TestRun.ResultSummary.Counters
 $summary.tests=@{total=[int]$c.total;passed=[int]$c.passed;failed=[int]$c.failed;notExecuted=[int]$c.notExecuted};$summary.trxSha256=Sha $trxPath
 $actual=@($trx.TestRun.Results.UnitTestResult | ForEach-Object {$_.testName})
 $expectedCounts=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal);$actualCounts=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
 foreach($name in $selected){if(-not $expectedCounts.ContainsKey($name)){$expectedCounts[$name]=0};$expectedCounts[$name]++}
 foreach($name in $actual){if(-not $actualCounts.ContainsKey($name)){$actualCounts[$name]=0};$actualCounts[$name]++}
 if($expectedCounts.Count -ne $actualCounts.Count){throw 'TRX name multiset differs'}
 foreach($name in $expectedCounts.Keys){if(-not $actualCounts.ContainsKey($name) -or $expectedCounts[$name] -ne $actualCounts[$name]){throw 'TRX name multiset differs'}}
 if($code -ne 0 -or $summary.tests.total -ne $expected -or $summary.tests.passed -ne $expected -or @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed').Count){throw 'Selected native tests failed; preserve TRX and logs'}
 $summary.passed=$true
}catch{$summary.error=$_.Exception.Message;throw}
finally{
 foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}
 $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Evidence 'summary.json')
}
$summary.tests | ConvertTo-Json
