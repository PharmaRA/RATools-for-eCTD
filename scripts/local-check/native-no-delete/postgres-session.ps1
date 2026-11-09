[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence,[Parameter(Mandatory)][string]$BuildRun,[Parameter(Mandatory)][string]$Ready,[Parameter(Mandatory)][string]$ProfileRoot,[Parameter(Mandatory)][string]$IdentityProbe,[Parameter(Mandatory)][string]$OutputManifest,[Parameter(Mandatory)][ValidatePattern('^/dev/shm/ratools-wpg/[a-f0-9]{32}$')][string]$RemoteRoot,[string]$SshProxyPath,[string]$SshProxySha256,[switch]$PythonRelay)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
if(-not $IsWindows -or -not $Evidence.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase)){throw 'Windows task root required'}
$readyRecord=Get-Content -LiteralPath $Ready -Raw | ConvertFrom-Json
if($readyRecord.database -notmatch '^qa07_windows_[0-9a-f]{32}$' -or $readyRecord.port -lt 1024 -or $readyRecord.port -gt 65535){throw 'Invalid task server identity'}
if($readyRecord.runId -notmatch '^[a-f0-9]{32}$' -or $RemoteRoot -cne ('/dev/shm/ratools-wpg/'+$readyRecord.runId)){throw 'Remote run identity mismatch'}
$clock=[Diagnostics.Stopwatch]::StartNew();$summary=@{passed=$false;runId=$readyRecord.runId;steps=@();connectionValueRetained=$false};$tunnel=$null
$dotnet=Join-Path $repo '.artifacts/tools/dotnet-8.0.423/dotnet.exe'
$build=Join-Path $BuildRun 'build';$assembly=Join-Path $build 'bin/RATools.Tests/Release/net8.0/RATools.Tests.dll'
$migrator=Join-Path $build 'bin/RATools.DatabaseMigrator/Release/net8.0/RATools.DatabaseMigrator.dll'
$manifestPath=Join-Path $build 'source-manifest.json';$buildSummary=Get-Content -LiteralPath (Join-Path $build 'summary.json') -Raw | ConvertFrom-Json
if($buildSummary.exitCode -ne 0 -or (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $buildSummary.sourceManifestSha256){throw 'PG test build manifest mismatch'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
foreach($entry in $manifest.GetEnumerator()){if((Get-FileHash -LiteralPath (Join-Path $repo $entry.Key) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value){throw 'PG test build input changed'}}
$bindings=@{sourceManifestSha256=$buildSummary.sourceManifestSha256};foreach($path in @($assembly,$migrator,$IdentityProbe,(Join-Path $repo 'scripts/pdf_content_worker.py'),(Join-Path $repo '.artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe'),'C:\Program Files\Python311\python.exe')){$bindings[[IO.Path]::GetFileName($path)]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
$bindings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'input-bindings.json')
$outputs=Get-Content -LiteralPath $OutputManifest -Raw | ConvertFrom-Json -AsHashtable
foreach($entry in $outputs.GetEnumerator()){if((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value){throw 'PG direct dependency output changed'}}
$saved=@{};foreach($name in @('RATOOLS_TEST_POSTGRES','ConnectionStrings__PostgreSql','ConnectionStrings:PostgreSql','FileSecrets__ApiKeyPath','FileSecrets__PostgreSqlPasswordPath','FileSecrets:ApiKeyPath','FileSecrets:PostgreSqlPasswordPath','RATOOLS_TEST_REPOSITORY_ROOT','TEMP','TMP')){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
function Execute([string]$Name,[string[]]$Arguments,[int]$Timeout=100){
 $remaining=360-[int]$clock.Elapsed.TotalSeconds;if($remaining -le 0){throw 'Local PG session deadline'}
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$dotnet;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$repo
 foreach($key in @('ConnectionStrings:PostgreSql','FileSecrets__ApiKeyPath','FileSecrets__PostgreSqlPasswordPath','FileSecrets:ApiKeyPath','FileSecrets:PostgreSqlPasswordPath','DOTNET_STARTUP_HOOKS','RATOOLS_EXCEPTION_RECEIPT')){$start.Environment.Remove($key) | Out-Null}
 $start.Environment['RATOOLS_TEST_POSTGRES']=[Environment]::GetEnvironmentVariable('RATOOLS_TEST_POSTGRES')
 $start.Environment['ConnectionStrings__PostgreSql']=$start.Environment['RATOOLS_TEST_POSTGRES']
 $start.Environment['RATOOLS_TEST_REPOSITORY_ROOT']=$repo
 if([string]::IsNullOrWhiteSpace($start.Environment['ConnectionStrings__PostgreSql'])){throw 'Task child connection missing'}
 foreach($a in $Arguments){$start.ArgumentList.Add($a)}
 $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 try{
  if(-not $process.WaitForExit([Math]::Min($remaining,$Timeout)*1000)){$process.Kill($true);$process.WaitForExit();$script:summary.steps+=@{name=$Name;deadlineExceeded=$true;childConnectionPresent=$true;colonAliasPresent=$start.Environment.ContainsKey('ConnectionStrings:PostgreSql')};throw "Stage deadline: $Name"}
  $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult()
  # Logs can contain provider diagnostics. Retain only accepted structured output,
  # never a connection string or uncontrolled migration error text.
  $script:summary.steps+=@{name=$Name;exitCode=$process.ExitCode;childConnectionPresent=$true;colonAliasPresent=$start.Environment.ContainsKey('ConnectionStrings:PostgreSql');stderrPresent=([bool]$err);stderrCategories=@{connection=($err -match '(?i)connection|socket|network|broken pipe');timeout=($err -match '(?i)timeout|timed out|deadline');migration=($err -match '(?i)migrat|schema|relation|database');permission=($err -match '(?i)permission|denied|access');duplicate=($err -match '(?i)duplicate|already exists|unique');cancellation=($err -match '(?i)cancel|canceled|cancelled');fatal=($err -match '(?i)fatal|unhandled|exception')};stdoutCategories=@{current=($out -match '(?i)schema is current');applied=($out -match '(?i)Applied [0-9]+ database migration');json=($out.TrimStart().StartsWith('{'))}}
  if($process.ExitCode -ne 0){
   if($Name -in @('migrate','migrate-repeat')){
    $diag=Join-Path $repo '.artifacts/local-check/pg-diag-1/MigratorDiagnostic.dll'
    $bindings=Get-Content -LiteralPath (Join-Path $repo '.artifacts/local-check/pg-diag-1/bindings.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach($binding in $bindings.GetEnumerator()){if((Get-FileHash -LiteralPath $binding.Key -Algorithm SHA256).Hash.ToLowerInvariant() -ne $binding.Value){throw 'Migration diagnostic binding mismatch'}}
    try{$null=Execute 'diagnostic-only' @($diag,$migrator,(Join-Path $Evidence 'migration-exception.json')) 60}catch{}
   }
   throw "Stage failed: $Name"
  }
  return $out
 }finally{if(-not $process.HasExited){$process.Kill($true);$process.WaitForExit()};$process.Dispose()}
}
function Stop-Remote {
 $script=@"
import pathlib,json,time
r=pathlib.Path('$RemoteRoot'); wanted={'runId':'$($readyRecord.runId)'}; stop=r/'stop-requested'
if stop.exists():
 assert not stop.is_symlink() and json.loads(stop.read_text())==wanted
else:
 with stop.open('x') as f: json.dump(wanted,f)
deadline=time.monotonic()+35
while time.monotonic()<deadline and not (r/'manager.json').exists(): time.sleep(.2)
m=json.loads((r/'manager.json').read_text());s=json.loads((r/'results/summary.json').read_text())
print(json.dumps({'managerExit':m['exitCode'],'stopped':m['stopped'],'serviceExit':s['exitCode'],'ownedStop':s.get('stopReason')=='owned-marker','serverExit':s.get('serverExitCode'),'nativeEmpty':s.get('nativeProcessesRemaining')==[],'serverGroupEmpty':s.get('serverGroupRemaining')==[]}))
"@
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script));$command="python3 -c 'import base64;exec(base64.b64decode(`"$encoded`"))'"
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe';$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.Environment['INSTABOX_NO_UPDATE_CHECK']='1'
 $start.Environment['USERPROFILE']=Join-Path $ProfileRoot 'profile';$start.Environment['APPDATA']=Join-Path $ProfileRoot 'profile/AppData';$start.Environment['LOCALAPPDATA']=Join-Path $ProfileRoot 'profile/AppData'
 foreach($arg in @('exec','m_agquz43gf246rsedwy3bkathci','--json','--timeout','45','--',$command)){$start.ArgumentList.Add($arg)}
 $p=[Diagnostics.Process]::Start($start);$out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
 try{
  if(-not $p.WaitForExit(55000)){$p.Kill($true);$p.WaitForExit();throw 'Remote stop transport deadline'}
  $result=$out.GetAwaiter().GetResult() | ConvertFrom-Json
  if($p.ExitCode -ne 0 -or $result.exitCode -ne 0 -or $result.timedOut -cne $false -or $result.truncated -cne $false){throw 'Remote stop receipt unavailable'}
  $state=$result.stdout | ConvertFrom-Json;$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'remote-stop.json')
  if($state.managerExit -ne 0 -or $state.serviceExit -ne 0 -or $state.serverExit -ne 0 -or $state.stopped -ne $true -or $state.ownedStop -ne $true -or $state.nativeEmpty -ne $true -or $state.serverGroupEmpty -ne $true){throw 'Remote stop was not clean and owned'}
 }finally{if(-not $p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
}
try {
 foreach($name in @('ConnectionStrings:PostgreSql','FileSecrets__ApiKeyPath','FileSecrets__PostgreSqlPasswordPath','FileSecrets:ApiKeyPath','FileSecrets:PostgreSqlPasswordPath')){[Environment]::SetEnvironmentVariable($name,$null)}
 [Environment]::SetEnvironmentVariable('RATOOLS_TEST_REPOSITORY_ROOT',$repo)
 $temp=Join-Path $Evidence 'temp';New-Item -ItemType Directory -Path $temp | Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $profile=Join-Path $ProfileRoot 'profile';$appdata=Join-Path $profile 'AppData';New-Item -ItemType Directory -Path $profile,$appdata -Force | Out-Null
 $reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop()
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe';$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $start.Environment['INSTABOX_NO_UPDATE_CHECK']='1';$start.Environment['USERPROFILE']=$profile;$start.Environment['APPDATA']=$appdata;$start.Environment['LOCALAPPDATA']=$appdata
 if($SshProxyPath){
  $SshProxyPath=[IO.Path]::GetFullPath($SshProxyPath)
  if(-not $SshProxyPath.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($SshProxyPath) -cne 'ssh.exe' -or (Get-FileHash -LiteralPath $SshProxyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $SshProxySha256){throw 'Task SSH proxy binding mismatch'}
  for($p=$SshProxyPath;$p;$p=[IO.Path]::GetDirectoryName($p)){if((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked SSH proxy refused'}}
  $start.Environment['PATH']=[IO.Path]::GetDirectoryName($SshProxyPath)+';'+$start.Environment['PATH'];$summary.sshProxySha256=$SshProxySha256
  $start.Environment.Remove('RATOOLS_SSH_DIAGNOSTICS') | Out-Null
  if($PythonRelay){$start.Environment['RATOOLS_SSH_PYTHON_RELAY']=Join-Path $PSScriptRoot 'ssh-socks-relay.py';$summary.pythonRelay=$true}else{$start.Environment.Remove('RATOOLS_SSH_PYTHON_RELAY') | Out-Null}
 }
 foreach($argument in @('port-forward','m_agquz43gf246rsedwy3bkathci','--remote',[string]$readyRecord.port,'--local',[string]$port,'--address','127.0.0.1')){$start.ArgumentList.Add($argument)}
 $tunnel=[Diagnostics.Process]::Start($start);$discardOut=$tunnel.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null);$tunnelError=$tunnel.StandardError.ReadToEndAsync()
 $tcpReady=$false;$tunnelClock=[Diagnostics.Stopwatch]::StartNew()
 while($tunnelClock.Elapsed.TotalSeconds -lt 40 -and -not $tunnel.HasExited){$client=[Net.Sockets.TcpClient]::new();try{if($client.ConnectAsync([Net.IPAddress]::Loopback,$port).Wait(500)){$tcpReady=$true;break}}catch{}finally{$client.Dispose()};Start-Sleep -Milliseconds 200}
 $summary.tunnelReady=$tcpReady;$summary.tunnelWaitMilliseconds=$tunnelClock.ElapsedMilliseconds
 if(-not $tcpReady){throw 'Task tunnel was not established'}
 $connection="Host=127.0.0.1;Port=$port;Database=$($readyRecord.database);Username=qa07;Pooling=false;Timeout=10"
 $env:RATOOLS_TEST_POSTGRES=$connection;$env:ConnectionStrings__PostgreSql=$connection;$connection=$null
 $null=Execute 'identity' @($IdentityProbe,$Ready,(Join-Path $Evidence 'identity.json')) 30
 $identity=Get-Content -LiteralPath (Join-Path $Evidence 'identity.json') -Raw | ConvertFrom-Json;if($identity.matched -ne $true -or $identity.runId -ne $readyRecord.runId){throw 'Connected database identity mismatch'}
 $first=Execute 'migrate' @($migrator) 200
 if($first.Trim() -cne 'Applied 31 database migration(s); schema is current.'){throw 'Unexpected initial migration count'}
 $second=Execute 'migrate-repeat' @($migrator) 40
 if($second.Trim() -cne 'Database schema is current; no migrations were applied.'){throw 'Repeated migration did not confirm current schema'}
 @{initial=31;repeat=0} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'migration-counts.json')
 $discovery=Execute 'discovery' @('vstest',$assembly,'/ListTests') 40
 $prefix='RATools.Tests.DocumentProcessing.PostgresPdfContentTests.'
 $selected=@($discovery -split '\r?\n' | ForEach-Object {$_.Trim()} | Where-Object {$_.StartsWith($prefix,[StringComparison]::Ordinal)})
 if($selected.Count -ne 2){throw 'PG content discovery count differs'}
 $selected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'selected-names.json')
 $results=Join-Path $Evidence 'results';New-Item -ItemType Directory -Path $results | Out-Null
 $null=Execute 'postgres-content' @('vstest',$assembly,('/TestCaseFilter:FullyQualifiedName~'+$prefix),'/Logger:trx;LogFileName=postgres-content.trx',('/ResultsDirectory:'+$results)) 180
 [xml]$trx=Get-Content -LiteralPath (Join-Path $results 'postgres-content.trx') -Raw
 $actual=@($trx.TestRun.Results.UnitTestResult | ForEach-Object {$_.testName})
 if($actual.Count -ne 2 -or (Compare-Object ($selected | Sort-Object) ($actual | Sort-Object)) -or @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed').Count){throw 'PG content TRX selection/outcome differs'}
 $summary.tests=@{total=2;passed=2;failed=0;notExecuted=0};$summary.trxSha256=(Get-FileHash -LiteralPath (Join-Path $results 'postgres-content.trx') -Algorithm SHA256).Hash.ToLowerInvariant();$summary.passed=$true
}catch{$summary.errorType=$_.Exception.GetType().Name;$summary.errorLine=$_.InvocationInfo.ScriptLineNumber;throw 'PG stage failed; inspect structured evidence'}
finally{
 try{try{Stop-Remote;$summary.remoteCleanStop=$true}catch{$summary.passed=$false;$summary.remoteStopErrorType=$_.Exception.GetType().Name}}
 finally{try{if($tunnel){
  $summary.tunnelExitedBeforeCleanup=$tunnel.HasExited
  if(-not $tunnel.HasExited){$tunnel.Kill($true)};$summary.tunnelRootStopped=$tunnel.WaitForExit(10000)
  if($summary.tunnelRootStopped){
   $summary.tunnelExitCode=$tunnel.ExitCode;$diagnostic=$tunnelError.GetAwaiter().GetResult()
   $summary.tunnelErrorCategories=@{permission=($diagnostic -match '(?i)permission|access.{0,15}denied');ssh=($diagnostic -match '(?i)ssh');network=($diagnostic -match '(?i)connect|network|resolve|timeout');authentication=($diagnostic -match '(?i)authentication|unauthorized|credential|certificate');fileOperation=($diagnostic -match '(?i)rename|unlink|remove|mkdir|open ');missingCommand=($diagnostic -match '(?i)not found|not recognized|cannot find');forwarding=($diagnostic -match '(?i)forward|listen|bind|administratively prohibited')}
   $diagnostic=$null
  };$tunnel.Dispose()
 }}
 finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])};$summary.elapsedMilliseconds=$clock.ElapsedMilliseconds;$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Evidence 'summary.json')}
 }
}
if(-not $summary.passed){throw 'PG acceptance or cleanup incomplete'}
