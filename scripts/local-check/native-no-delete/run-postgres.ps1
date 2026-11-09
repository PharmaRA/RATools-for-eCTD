[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence,[Parameter(Mandatory)][string]$BuildRun,[Parameter(Mandatory)][string]$IdentityProbe,[Parameter(Mandatory)][string]$SshProxyPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
if(-not $Evidence.StartsWith((Join-Path $repo '.artifacts/local-check/'),[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $Evidence)){throw 'Fresh PG task evidence required'}
for($p=[IO.Path]::GetDirectoryName($Evidence);$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked evidence path refused'}}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$profile=Join-Path $Evidence 'f';New-Item -ItemType Directory -Path $profile | Out-Null
$sources=@('NativeNoDelete.cs','RetainedMovePolicy.cs','postgres-check.ps1','postgres-session.ps1','PostgresIdentityProbe.cs','SshProxy.cs','ssh-socks-relay.py','MigratorDiagnostic.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$sources+=@('no-delete.py','windows-postgres-service.py','windows-postgres-manager.py') | ForEach-Object {Join-Path $repo ('scripts/remote-check/'+$_)}
$cli=Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe'
$hashes=@{};foreach($path in ($sources+@($PSCommandPath,$IdentityProbe,$SshProxyPath,$cli))){$hashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
if($hashes[$cli] -cne 'e3c91d8b98c924bff5cf32f7aa17a8bc8360ad4db173ce448c77c152134a2cfb'){throw 'CLI binding changed'}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'launch-bindings.json')
Add-Type -Path @((Join-Path $PSScriptRoot 'NativeNoDelete.cs'),(Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'))
$arguments=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'postgres-check.ps1'),'-Evidence',$Evidence,'-BuildRun',$BuildRun,'-ProfileRoot',$profile,'-IdentityProbe',$IdentityProbe,'-SshProxyPath',$SshProxyPath,'-SshProxySha256',$hashes[$SshProxyPath],'-PythonRelay')
$receipt=Join-Path $Evidence 'runner.process.json'
$code=[NativeNoDelete]::Run((Get-Process -Id $PID).Path,$arguments,$repo,(Join-Path $Evidence 'runner.log'),$receipt,600,'none',$null,$profile,$cli,$hashes[$cli])
$state=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
if($code -ne 0 -or $state.processTreeStopped -ne $true -or $state.guardedProcesses -ne $state.totalJobProcesses){throw 'Guarded PostgreSQL acceptance failed; evidence retained'}
$summary=Get-Content -LiteralPath (Join-Path $Evidence 'coordinator-summary.json') -Raw | ConvertFrom-Json
if($summary.passed -ne $true){throw 'PostgreSQL coordinator acceptance incomplete'}
Write-Output 'PostgreSQL selected acceptance passed; review receipts before accounting.'
