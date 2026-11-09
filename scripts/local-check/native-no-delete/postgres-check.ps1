[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence,[Parameter(Mandatory)][string]$BuildRun,[Parameter(Mandatory)][string]$ProfileRoot,[Parameter(Mandatory)][string]$IdentityProbe,[string]$SshProxyPath,[string]$SshProxySha256,[switch]$PythonRelay)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence);$BuildRun=[IO.Path]::GetFullPath($BuildRun);$ProfileRoot=[IO.Path]::GetFullPath($ProfileRoot)
$allowed=Join-Path $repo '.artifacts/local-check/'
foreach($path in @($Evidence,$BuildRun,$ProfileRoot,[IO.Path]::GetDirectoryName($IdentityProbe))){if(-not $path.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'PG coordinator path outside task area'}}
$runId=[guid]::NewGuid().ToString('N');$remote='/dev/shm/ratools-wpg/'+$runId
$platform='/dev/shm/ratools-remote/20261108T210327-38040602f8864ca7b7fdfaa87464e554'
$cli=Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe'
$summary=@{passed=$false;runId=$runId;remote=$remote;serviceStarted=$false;outerGuardRequired=$true};$manager=$null;$managerOut=$null;$managerErr=$null
function Start-Cli([string[]]$Arguments){
 $s=[Diagnostics.ProcessStartInfo]::new();$s.FileName=$cli;$s.UseShellExecute=$false;$s.CreateNoWindow=$true;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true;$s.Environment['INSTABOX_NO_UPDATE_CHECK']='1'
 $s.Environment['USERPROFILE']=Join-Path $ProfileRoot 'profile';$s.Environment['APPDATA']=Join-Path $ProfileRoot 'profile/AppData';$s.Environment['LOCALAPPDATA']=Join-Path $ProfileRoot 'profile/AppData'
 foreach($a in $Arguments){$s.ArgumentList.Add($a)}
 [Diagnostics.Process]::Start($s)
}
function Invoke-TaskCli([string[]]$Arguments,[int]$Timeout=60){
 $p=Start-Cli $Arguments;$out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
 try{if(-not $p.WaitForExit($Timeout*1000)){$p.Kill($true);$p.WaitForExit();throw 'CLI transport deadline'};$text=$out.GetAwaiter().GetResult();if($p.ExitCode -ne 0){throw 'CLI transport failed'};return ($text | ConvertFrom-Json)}
 finally{if(-not $p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
}
function Remote([string]$Code,[int]$Timeout=20){
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Code));$command="python3 -c 'import base64;exec(base64.b64decode(`"$encoded`"))'"
 $result=Invoke-TaskCli @('exec','m_agquz43gf246rsedwy3bkathci','--json','--timeout',[string]$Timeout,'--',$command) ($Timeout+15)
 if($result.exitCode -ne 0 -or $result.timedOut -cne $false -or $result.truncated -cne $false -or $result.oomKilled -cne $false){throw 'Remote command incomplete'}
 return ($result.stdout | ConvertFrom-Json)
}
try{
 $prepare=@"
import pathlib,json,hashlib,os
p=pathlib.Path('$platform');root=pathlib.Path('$remote')
assert not (p/'postgres-task/cluster/postmaster.pid').exists()
files=['postgres-tools/postgresql-16/usr/lib/postgresql/16/bin/postgres','postgres-tools/postgresql-client-16/usr/lib/postgresql/16/bin/psql','postgres-tools/recycle-postgres.so']
hashes={}
for name in files:
 f=p/name;assert f.is_file() and not f.is_symlink();hashes[name]=hashlib.sha256(f.read_bytes()).hexdigest()
root.mkdir(parents=True,exist_ok=False)
print(json.dumps({'platformHashes':hashes}))
"@
 $platformReceipt=Remote $prepare
 $config=@{runId=$runId;platformRoot=$platform;platformHashes=$platformReceipt.platformHashes};$configPath=Join-Path $Evidence 'service-config.json';$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath
 $sources=@{'no-delete.py'=(Join-Path $repo 'scripts/remote-check/no-delete.py');'windows-postgres-service.py'=(Join-Path $repo 'scripts/remote-check/windows-postgres-service.py');'windows-postgres-manager.py'=(Join-Path $repo 'scripts/remote-check/windows-postgres-manager.py');'service-config.json'=$configPath}
 $hashes=@{};$archive=Join-Path $Evidence 'source.zip';$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($entry in $sources.GetEnumerator()){$hashes[$entry.Key]=(Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant();[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Value,$entry.Key) | Out-Null}}finally{$zip.Dispose()}
 $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'service-inputs.json')
 $digest=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();$summary.archiveSha256=$digest
 $null=Invoke-TaskCli @('cp','-p',$archive,('m_agquz43gf246rsedwy3bkathci:'+$remote+'/source.zip'),'--json')
 $null=Remote "import pathlib,hashlib,zipfile,json;p=pathlib.Path('$remote/source.zip');assert hashlib.sha256(p.read_bytes()).hexdigest()=='$digest';zipfile.ZipFile(p).extractall(p.parent);print(json.dumps({'extracted':True}))"
 $outputs=@{}
 foreach($directory in @((Join-Path $BuildRun 'build/bin/RATools.Tests/Release/net8.0'),(Join-Path $BuildRun 'build/bin/RATools.DatabaseMigrator/Release/net8.0'),(Split-Path -Parent $IdentityProbe))){Get-ChildItem -LiteralPath $directory -File | ForEach-Object {if($_.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked dependency refused'};$outputs[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}
 $outputManifest=Join-Path $Evidence 'output-manifest.json';$outputs | ConvertTo-Json | Set-Content -LiteralPath $outputManifest
 $command='python3 no-delete.py python3 windows-postgres-manager.py'
 $manager=Start-Cli @('exec','m_agquz43gf246rsedwy3bkathci','--json','--timeout','600','--cwd',$remote,'--',$command)
 $managerOut=$manager.StandardOutput.ReadToEndAsync();$managerErr=$manager.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null);$summary.serviceStarted=$true
 $ready=$null;$startup=[Diagnostics.Stopwatch]::StartNew()
 while($startup.Elapsed.TotalSeconds -lt 60 -and -not $manager.HasExited){
  $state=Remote "import pathlib,json;p=pathlib.Path('$remote/results/ready.json');print(json.dumps({'ready':json.loads(p.read_text()) if p.is_file() else None}))"
  if($state.ready){$ready=$state.ready;break};Start-Sleep -Milliseconds 200
 }
 if(-not $ready -or $ready.runId -cne $runId -or $ready.driverSha256 -cne $hashes['windows-postgres-service.py'] -or $ready.guardSha256 -cne $hashes['no-delete.py']){throw 'Remote ready source/run identity mismatch'}
 $readyPath=Join-Path $Evidence 'ready.json';$ready | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $readyPath
 $session=Join-Path $Evidence 'session';New-Item -ItemType Directory -Path $session | Out-Null
 & (Join-Path $PSScriptRoot 'postgres-session.ps1') -Evidence $session -BuildRun $BuildRun -Ready $readyPath -ProfileRoot $ProfileRoot -IdentityProbe $IdentityProbe -OutputManifest $outputManifest -RemoteRoot $remote -SshProxyPath $SshProxyPath -SshProxySha256 $SshProxySha256 -PythonRelay:$PythonRelay
 $accepted=Get-Content -LiteralPath (Join-Path $session 'summary.json') -Raw | ConvertFrom-Json
 if($accepted.passed -ne $true -or $accepted.remoteCleanStop -ne $true -or $accepted.tunnelRootStopped -ne $true){throw 'PG local acceptance incomplete'}
 $summary.passed=$true
}catch{$summary.errorType=$_.Exception.GetType().Name;$summary.errorLine=$_.InvocationInfo.ScriptLineNumber}
finally{
 if($summary.serviceStarted){
  try{
   $stop=@"
import pathlib,json,time
r=pathlib.Path('$remote');p=r/'stop-requested';wanted={'runId':'$runId'}
if p.exists(): assert not p.is_symlink() and json.loads(p.read_text())==wanted
else:
 with p.open('x') as f: json.dump(wanted,f)
deadline=time.monotonic()+45
while not (r/'manager.json').exists() and time.monotonic()<deadline: time.sleep(.2)
m=json.loads((r/'manager.json').read_text());s=json.loads((r/'results/summary.json').read_text())
print(json.dumps({'manager':m,'service':s}))
"@
   $closed=Remote $stop 55;$closed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Evidence 'remote-final.json')
   if($closed.manager.exitCode -ne 0 -or $closed.manager.stopped -ne $true -or $closed.service.exitCode -ne 0 -or $closed.service.stopReason -cne 'owned-marker'){throw 'Remote PG lifecycle failed'}
   $null=Invoke-TaskCli @('cp','-r',('m_agquz43gf246rsedwy3bkathci:'+$remote+'/results'),$Evidence,'--json') 60
  }catch{$summary.passed=$false;$summary.remoteCleanupErrorType=$_.Exception.GetType().Name;$summary.remoteCleanupErrorLine=$_.InvocationInfo.ScriptLineNumber}
 }
 if($manager){
  try{
   if(-not $manager.WaitForExit(15000)){$manager.Kill($true);$manager.WaitForExit();$summary.passed=$false}
   $transport=$managerOut.GetAwaiter().GetResult() | ConvertFrom-Json
   $summary.remoteTransport=@{cliExit=$manager.ExitCode;exitCode=$transport.exitCode;timedOut=$transport.timedOut;truncated=$transport.truncated;oomKilled=$transport.oomKilled}
   if($manager.ExitCode -ne 0 -or $transport.exitCode -ne 0 -or $transport.timedOut -cne $false -or $transport.truncated -cne $false -or $transport.oomKilled -cne $false){$summary.passed=$false}
  }catch{$summary.passed=$false;$summary.transportError=$_.Exception.GetType().Name}finally{$manager.Dispose()}
 }
 $summary | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $Evidence 'coordinator-summary.json')
}
if(-not $summary.passed){throw 'PG coordinated acceptance failed; retained evidence requires inspection'}
