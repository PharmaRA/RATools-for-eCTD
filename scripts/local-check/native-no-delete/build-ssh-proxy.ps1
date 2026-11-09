[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
if(-not $Evidence.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $Evidence)){throw 'Fresh task evidence required'}
for($p=[IO.Path]::GetDirectoryName($Evidence);$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked build path refused'}}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$guard=Join-Path $PSScriptRoot 'NativeNoDelete.cs';$policy=Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'
Add-Type -Path @($guard,$policy)
$sdk=Join-Path $repo '.artifacts/tools/dotnet-8.0.423';$compiler=Join-Path $sdk 'sdk/8.0.423/Roslyn/bincore/csc.dll'
$exe=Join-Path $Evidence 'ssh.exe';$source=Join-Path $PSScriptRoot 'SshProxy.cs';$tests=Join-Path $PSScriptRoot 'SshProxyTests.cs'
$references=@('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object {Join-Path $env:SystemRoot ('Microsoft.NET/Framework64/v4.0.30319/'+$_)}
$arguments=@($compiler,'/nologo','/target:exe','/platform:x64','/warnaserror+','/nostdlib+','/nullable:disable',('/out:'+$exe))
$arguments+=@($references | ForEach-Object {'/r:'+$_});$arguments+=@($source,$tests)
foreach($stage in @('compile','test')){
 $receipt=Join-Path $Evidence ($stage+'.process.json');$log=Join-Path $Evidence ($stage+'.log')
 if($stage -eq 'compile'){$code=[NativeNoDelete]::Run((Join-Path $sdk 'dotnet.exe'),$arguments,$repo,$log,$receipt,60)}
 else{$code=[NativeNoDelete]::Run($exe,@('--self-test'),$repo,$log,$receipt,60)}
 $state=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
 if($code -ne 0 -or $state.processTreeStopped -ne $true -or $state.guardedProcesses -ne $state.totalJobProcesses){throw 'Guarded proxy stage failed; evidence retained'}
}
$bindings=@{};foreach($path in (@($PSCommandPath,$guard,$policy,$source,$tests,$exe,$compiler,(Join-Path $env:SystemRoot 'System32/OpenSSH/ssh.exe'))+$references)){$bindings[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
$bindings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'bindings.json')
Get-Content -LiteralPath (Join-Path $Evidence 'test.log')
