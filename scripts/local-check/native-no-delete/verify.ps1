[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
$allowed=Join-Path $repo '.artifacts/local-check/'
if(-not $Evidence.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence outside local-check'}
$build=Get-Content -LiteralPath (Join-Path $Evidence 'build.json') -Raw | ConvertFrom-Json
$guard=Join-Path $PSScriptRoot 'NativeNoDelete.cs';$probe=Join-Path $Evidence 'GuardProbe.dll'
if((Get-FileHash -LiteralPath $guard -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.guardSourceSha256 -or (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.assemblySha256){throw 'Guard/source build identity changed'}
Add-Type -Path @($guard,(Join-Path $PSScriptRoot 'GuardPolicyTests.cs'))
if([GuardPolicyTests]::Run() -ne 25){throw 'Policy preflight failed'}
$dotnet=Join-Path $repo '.artifacts/tools/dotnet-8.0.423/dotnet.exe'
$summary=@{passed=$false;steps=@();scope='Windows x64 guarded compiler and synthetic permission/process probes, not remaining19 native acceptance'}
try {
 foreach($case in @(
    @{name='tree';mode='parent';timeout=90;fault='none';expected=0;guarded=3},
    @{name='exception';mode='exception';timeout=30;fault='none';expected=42;guarded=1},
    @{name='before-patch';mode='parent';timeout=30;fault='before-patch';expected=1;guarded=0},
    @{name='after-patch';mode='parent';timeout=30;fault='after-patch';expected=1;guarded=1},
    @{name='timeout';mode='wait';timeout=4;fault='none';expected=124;guarded=1}
 )) {
    $run=Join-Path $Evidence $case.name
    if(Test-Path -LiteralPath $run){throw 'Existing verification run refused'}
    New-Item -ItemType Directory -Path $run | Out-Null
    $receipt=Join-Path $run 'process.json';$log=Join-Path $run 'probe.log'
    $code=[NativeNoDelete]::Run($dotnet,@($probe,$case.mode,$run),$repo,$log,$receipt,$case.timeout,$case.fault)
    $result=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if($code -ne $case.expected -or $result.exitCode -ne $code -or $result.processTreeStopped -ne $true -or $result.assignedBeforeResume -ne $true){throw "Unexpected verification outcome: $($case.name)"}
    $created=@($result.events | Where-Object kind -eq 'created')
    if(@($created | Where-Object {$_.image -notin @('dotnet.exe','conhost.exe')}).Count){throw 'Unexpected process image'}
    if($case.fault -eq 'none'){
      if($result.totalJobProcesses -ne $result.guardedProcesses -or $created.Count -ne $result.guardedProcesses -or @($created | Where-Object image -eq 'dotnet.exe').Count -ne $case.guarded -or @($created | Where-Object image -eq 'conhost.exe').Count -gt $case.guarded){throw 'Job/guard/program process count mismatch'}
    }elseif($result.guardedProcesses -ne $case.guarded){throw 'Injected fault guard count mismatch'}
    if($case.name -eq 'tree'){
      $rows=@(Get-Content -LiteralPath $log | ForEach-Object {$_ | ConvertFrom-Json})
      if($rows.Count -ne 3 -or @($rows | Where-Object {$_.passed -ne $true -or $_.rejectionAndPassthroughChecks -ne 17 -or $_.validDeletionTargetUsed -ne $false}).Count){throw 'Incomplete parent/child/grandchild assertions'}
      foreach($name in @('parent','child','grandchild')){if((Get-Content -LiteralPath (Join-Path $run ($name+'-retained.txt')) -Raw) -cne 'retained'){throw 'Retained sentinel differs'}}
    }
    if($case.name -eq 'timeout' -and -not (Select-String -LiteralPath $log -SimpleMatch 'verified-before-wait' -Quiet)){throw 'Timeout occurred before guarded probe started'}
    if($case.name -in @('before-patch','after-patch','timeout') -and $result.forcedTreeStop -ne $true){throw 'Fault cleanup did not terminate its owned process'}
    $summary.steps+=@{name=$case.name;expectedExit=$case.expected;exitCode=$code;guarded=$result.guardedProcesses;jobProcesses=$result.totalJobProcesses;stopped=$result.processTreeStopped}
 }
 $summary.passed=$true
} finally {
 $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Evidence 'verification.json')
}
$summary | ConvertTo-Json -Depth 6
