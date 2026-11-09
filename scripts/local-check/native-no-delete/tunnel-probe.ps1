[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence,[Parameter(Mandatory)][string]$ProfileRoot,[string]$SshProxyPath,[string]$SshProxySha256,[switch]$PythonRelay)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
if(-not $Evidence.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase)){throw 'Probe evidence outside workspace'}
$reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop()
$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe'
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.Environment['INSTABOX_NO_UPDATE_CHECK']='1'
$profile=Join-Path $ProfileRoot 'profile';$appdata=Join-Path $profile 'AppData';New-Item -ItemType Directory -Path $profile,$appdata | Out-Null
$start.Environment['USERPROFILE']=$profile;$start.Environment['APPDATA']=$appdata;$start.Environment['LOCALAPPDATA']=$appdata
if($SshProxyPath){
 $SshProxyPath=[IO.Path]::GetFullPath($SshProxyPath)
 if(-not $SshProxyPath.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($SshProxyPath) -cne 'ssh.exe' -or (Get-FileHash -LiteralPath $SshProxyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $SshProxySha256){throw 'Task SSH proxy binding mismatch'}
 for($p=$SshProxyPath;$p;$p=[IO.Path]::GetDirectoryName($p)){if((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked SSH proxy refused'}}
 $start.Environment['PATH']=[IO.Path]::GetDirectoryName($SshProxyPath)+';'+$start.Environment['PATH']
 $start.Environment['RATOOLS_SSH_DIAGNOSTICS']='1'
 if($PythonRelay){$start.Environment['RATOOLS_SSH_PYTHON_RELAY']=Join-Path $PSScriptRoot 'ssh-socks-relay.py'}else{$start.Environment.Remove('RATOOLS_SSH_PYTHON_RELAY') | Out-Null}
}
foreach($argument in @('port-forward','m_agquz43gf246rsedwy3bkathci','--remote','22','--local',[string]$port,'--address','127.0.0.1')){$start.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::Start($start)
# Authentication diagnostics are consumed and discarded, never serialized.
$outDiscard=$process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null);$errorText=$process.StandardError.ReadToEndAsync()
$summary=@{passed=$false;loopbackOnly=$true;credentialOutputRetained=$false;sshProxySha256=$SshProxySha256}
try {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while($clock.Elapsed.TotalSeconds -lt 35 -and -not $process.HasExited){
  $client=[Net.Sockets.TcpClient]::new()
  try{
   $connect=$client.ConnectAsync([Net.IPAddress]::Loopback,$port)
   if($connect.Wait(500)){
    $stream=$client.GetStream();$stream.ReadTimeout=1000;$bytes=[byte[]]::new(128);$length=$stream.Read($bytes,0,$bytes.Length)
    if($length -ge 4 -and [Text.Encoding]::ASCII.GetString($bytes,0,4) -ceq 'SSH-'){$summary.passed=$true;break}
   }
  }catch [Net.Sockets.SocketException]{}catch [IO.IOException]{}catch [AggregateException]{}
  finally{$client.Dispose()}
  Start-Sleep -Milliseconds 200
 }
 if($process.HasExited){$summary.cliExitCode=$process.ExitCode}
}finally{
 if(-not $process.HasExited){$process.Kill($true)}
 if(-not $process.WaitForExit(10000)){throw 'Tunnel root did not stop'}
 $outDiscard.GetAwaiter().GetResult();$diagnostic=$errorText.GetAwaiter().GetResult()
 $summary.errorCategories=@{permission=($diagnostic -match '(?i)permission|access.{0,15}denied');ssh=($diagnostic -match '(?i)ssh');network=($diagnostic -match '(?i)connect|network|resolve|timeout');authentication=($diagnostic -match '(?i)authentication|unauthorized|credential|certificate');fileOperation=($diagnostic -match '(?i)rename|unlink|remove|mkdir|open ');missingCommand=($diagnostic -match '(?i)not found|not recognized|cannot find')}
 $summary.operationCategories=@{};foreach($term in @('rename','remove','unlink','mkdir','open','chmod','write','known_hosts','identity','certificate','config','cache','temp')){$summary.operationCategories[$term]=($diagnostic -match [regex]::Escape($term))}
 $summary.isolatedProfileReferenced=$diagnostic.Contains($profile)
 $summary.socksReady=$diagnostic.Contains('TASK_SOCKS_READY');$summary.proxyFailed=$diagnostic.Contains('Task SSH proxy failed:')
 $summary.sshOptionNames=@([regex]::Matches($diagnostic,'TASK_ARG (-[A-Za-z]+|destination|unknown-option)(?: ([A-Za-z]+))?') | ForEach-Object {$_.Groups[1].Value+':'+$_.Groups[2].Value})
 $summary.remotePrefix=@{ssh=$diagnostic.Contains('TASK_REMOTE_PREFIX ssh');http=$diagnostic.Contains('TASK_REMOTE_PREFIX http');other=$diagnostic.Contains('TASK_REMOTE_PREFIX other')}
 $summary.relayEnd=@{upstreamEof=$diagnostic.Contains('TASK_UPSTREAM_EOF');sendHalfClosed=$diagnostic.Contains('TASK_SEND_HALF_CLOSED');downstreamEof=$diagnostic.Contains('TASK_DOWNSTREAM_EOF');upstreamError=$diagnostic.Contains('TASK_UPSTREAM_ERROR');upstreamBeforeDownstream=($diagnostic.IndexOf('TASK_UPSTREAM_EOF') -ge 0 -and $diagnostic.IndexOf('TASK_DOWNSTREAM_EOF') -gt $diagnostic.IndexOf('TASK_UPSTREAM_EOF'))}
 $relayError=[regex]::Match($diagnostic,'TASK_UPSTREAM_ERROR ([A-Za-z]+Exception)');if($relayError.Success){$summary.relayErrorType=$relayError.Groups[1].Value}
 $summary.sshStages=@{localVersion=$diagnostic.Contains('Local version string');remoteVersion=$diagnostic.Contains('Remote protocol version');kexSent=$diagnostic.Contains('SSH2_MSG_KEXINIT sent');kexReceived=$diagnostic.Contains('SSH2_MSG_KEXINIT received');newKeys=$diagnostic.Contains('SSH2_MSG_NEWKEYS');authenticated=$diagnostic.Contains('Authenticated to');unsupported=($diagnostic -match '(?i)no matching');packetError=($diagnostic -match '(?i)Bad packet length|Corrupted MAC|message authentication code incorrect');pipeError=($diagnostic -match '(?i)broken pipe|ReadFile|read failed|write failed')}
 $counts=[regex]::Match($diagnostic,'TASK_SOCKS_BYTES ([0-9]+) ([0-9]+)');if($counts.Success){$summary.socksBytes=@{sent=[long]$counts.Groups[1].Value;received=[long]$counts.Groups[2].Value}}
 $summary.networkCategories=@{timeout=($diagnostic -match '(?i)timed out|timeout');refused=($diagnostic -match '(?i)connection refused');reset=($diagnostic -match '(?i)connection reset');closed=($diagnostic -match '(?i)connection closed');resolution=($diagnostic -match '(?i)resolve|name or service');route=($diagnostic -match '(?i)no route|unreachable');keyExchange=($diagnostic -match '(?i)kex_exchange|banner exchange')}
 $diagnostic=$null
 $summary.cliStopped=$process.HasExited;$process.Dispose()
 $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'tunnel-probe.json')
}
if(-not $summary.passed){exit 1}
