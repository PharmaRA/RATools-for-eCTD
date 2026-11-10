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
if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'RetainedMovePolicy.cs') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.retainedMovePolicySha256){throw 'Move policy identity changed'}
$inputs=Get-Content -LiteralPath (Join-Path $Evidence 'qualification-inputs.json') -Raw|ConvertFrom-Json -AsHashtable
foreach($entry in $inputs.GetEnumerator()){if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $entry.Key)).Hash.ToLowerInvariant() -ne $entry.Value){throw 'Qualification source identity changed'}}
if($inputs.Count -ne 8){throw 'Qualification input inventory incomplete'}
Add-Type -Path @($guard,(Join-Path $PSScriptRoot 'GuardPolicyTests.cs'),(Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'),(Join-Path $PSScriptRoot 'RetainedMoveTests.cs'),(Join-Path $PSScriptRoot 'FixtureRetentionTests.cs'))
if([GuardPolicyTests]::Run() -ne 25){throw 'Policy preflight failed'}
$dotnet=Join-Path $repo '.artifacts/tools/dotnet-8.0.423/dotnet.exe'
$summary=@{passed=$false;steps=@();scope='Windows x64 guarded compiler and synthetic permission/process probes, not remaining19 native acceptance'}
try {
 foreach($case in @(
    @{name='tree';mode='parent';timeout=90;fault='none';expected=0;guarded=3},
    @{name='exception';mode='exception';timeout=30;fault='none';expected=42;guarded=1},
    @{name='before-patch';mode='parent';timeout=30;fault='before-patch';expected=1;guarded=0},
    @{name='after-patch';mode='parent';timeout=30;fault='after-patch';expected=1;guarded=1},
    @{name='timeout';mode='wait';timeout=4;fault='none';expected=124;guarded=1},
    @{name='retained-move';mode='move';timeout=30;fault='none';expected=0;guarded=1;move=$true},
    @{name='fixture-retention';mode='fixture';timeout=60;fault='none';expected=0;guarded=1;fixture=$true},
    @{name='cli-initialization';mode='cli';timeout=30;fault='none';expected=0;guarded=1;cli=$true}
 )) {
    $run=Join-Path $Evidence $case.name
    if(Test-Path -LiteralPath $run){throw 'Existing verification run refused'}
    New-Item -ItemType Directory -Path $run | Out-Null
    $probeRoot=$run;$moveRoot=$null;$cliRoot=$null;$cliExe=$null;$cliSha=$null
    if($case.move){$moveRoot=Join-Path $run 'fixtures';New-Item -ItemType Directory -Path $moveRoot | Out-Null;$probeRoot=$moveRoot}
    if($case.fixture){$moveRoot=Join-Path $run 'fixtures';New-Item -ItemType Directory -Path $moveRoot | Out-Null;$probeRoot=$moveRoot}
    if($case.cli){$cliRoot=Join-Path $run 'fixtures';New-Item -ItemType Directory -Path $cliRoot | Out-Null;$probeRoot=$cliRoot;$cliExe=$dotnet;$cliSha=(Get-FileHash -LiteralPath $dotnet -Algorithm SHA256).Hash.ToLowerInvariant()}
    $receipt=Join-Path $run 'process.json';$log=Join-Path $run 'probe.log'
    $code=[NativeNoDelete]::Run($dotnet,@($probe,$case.mode,$probeRoot),$repo,$log,$receipt,$case.timeout,$case.fault,$moveRoot,$cliRoot,$cliExe,$cliSha)
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
    if($case.move){
      $decisions=@($result.events | Where-Object kind -eq 'move-decision')
      if(@($decisions | Where-Object allowed -eq $true).Count -ne 2 -or @($decisions | Where-Object allowed -eq $false).Count -lt 1){throw 'Move permission/refusal evidence incomplete'}
      $row=Get-Content -LiteralPath $log -Raw | ConvertFrom-Json
      if($row.passed -ne $true -or $row.rejectionAndPassthroughChecks -ne 17){throw 'Move probe did not complete existing guard assertions'}
    }
    if($case.cli){$decisions=@($result.events | Where-Object kind -eq 'move-decision');if(@($decisions | Where-Object allowed -eq $true).Count -ne 5 -or @($decisions | Where-Object allowed -eq $false).Count -lt 1){throw 'CLI initialization permission/refusal evidence incomplete'}}
    if($case.fixture){
      $decisions=@($result.events|Where-Object kind -eq 'move-decision');$accepted=@($decisions|Where-Object allowed -eq $true)
      $expected=@('package-capture-retention','package-upload-retention','package-missing-input-retention','package-fixture-retention')
      if($accepted.Count -ne 4 -or @($decisions|Where-Object {$_.allowed -eq $false -and $_.reason -eq 'unsupported-class-or-length'}).Count -ne 2 -or @($decisions|Where-Object {$_.allowed -eq $false -and $_.reason -ne 'unsupported-class-or-length'}).Count -ne 3 -or (@($accepted.relation|Sort-Object)-join ',') -ne (@($expected|Sort-Object)-join ',')){throw 'Fixture retention decisions incomplete'}
      $row=Get-Content -LiteralPath $log -Raw|ConvertFrom-Json;if(-not $row.passed -or $row.rejectionAndPassthroughChecks -ne 17){throw 'Fixture retention probe failed'}
    }
    if($case.name -in @('before-patch','after-patch','timeout') -and $result.forcedTreeStop -ne $true){throw 'Fault cleanup did not terminate its owned process'}
    $summary.steps+=@{name=$case.name;expectedExit=$case.expected;exitCode=$code;guarded=$result.guardedProcesses;jobProcesses=$result.totalJobProcesses;stopped=$result.processTreeStopped}
 }
 $summary.retainedMovePathChecks=[RetainedMoveTests]::Run((Join-Path $Evidence 'move-path-checks'))
 if($summary.retainedMovePathChecks -ne 14){throw 'Move path checks incomplete'}
 $summary.fixtureRetentionPathChecks=[FixtureRetentionTests]::Run((Join-Path $Evidence 'fixture-path-checks'))
 if($summary.fixtureRetentionPathChecks -ne 18){throw 'Fixture retention path checks incomplete'}
 $summary.qualificationInputsSha256=(Get-FileHash -LiteralPath (Join-Path $Evidence 'qualification-inputs.json')).Hash.ToLowerInvariant()
 $summary.passed=$true
} finally {
 $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Evidence 'verification.json')
}
$summary | ConvertTo-Json -Depth 6
