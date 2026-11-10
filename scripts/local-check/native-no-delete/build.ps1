[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$Evidence=[IO.Path]::GetFullPath($Evidence)
$allowed=Join-Path $repo '.artifacts/local-check/'
if(-not $Evidence.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence outside local-check'}
for($p=$Evidence;$p;$p=[IO.Path]::GetDirectoryName($p)){
    if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked build path refused'}
}
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
$sdk=Join-Path $repo '.artifacts/tools/dotnet-8.0.423'
$temp=Join-Path $Evidence ('compiler-temp-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$acl=Get-Acl -LiteralPath $temp
$backup=Join-Path $repo 'backups/qa-07-local-20261109/native-guard-build'
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$acl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::All) | Set-Content -LiteralPath (Join-Path $backup ((Split-Path -Leaf $temp)+'.sddl'))
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,([Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles),([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny))
Set-Acl -LiteralPath $temp -AclObject $acl
$oldTemp=$env:TEMP;$oldTmp=$env:TMP
try {
 $env:TEMP=$temp;$env:TMP=$temp
 $guard=Join-Path $PSScriptRoot 'NativeNoDelete.cs';$source=Join-Path $PSScriptRoot 'GuardProbe.cs';$assembly=Join-Path $Evidence 'GuardProbe.dll'
 if(Test-Path -LiteralPath $assembly){throw 'Existing build output refused'}
 Add-Type -Path @($guard,(Join-Path $PSScriptRoot 'GuardPolicyTests.cs'),(Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'),(Join-Path $PSScriptRoot 'RetainedMoveTests.cs'))
 if([GuardPolicyTests]::Run() -ne 25){throw 'Policy ABI preflight failed'}
 $compiler=Join-Path $sdk 'sdk/8.0.423/Roslyn/bincore/csc.dll'
 $args=@('/nologo','/target:exe','/langversion:12','/warnaserror+','/nullable:disable',('/out:'+$assembly))
 $args+=@(Get-ChildItem -LiteralPath (Join-Path $sdk 'packs/Microsoft.NETCore.App.Ref/8.0.29/ref/net8.0') -Filter '*.dll' -File | ForEach-Object {'/r:'+$_.FullName})
 $args+=@($guard,$source,(Join-Path $PSScriptRoot 'RetainedMovePolicy.cs'))
 $response=Join-Path $Evidence 'probe.rsp'
 $args | ForEach-Object {'"'+$_+'"'} | Set-Content -LiteralPath $response
 $code=[NativeNoDelete]::Run((Join-Path $sdk 'dotnet.exe'),@($compiler,('@'+$response)),$repo,(Join-Path $Evidence 'compile.log'),(Join-Path $Evidence 'compile-process.json'),120)
 if($code -ne 0){throw "Guarded probe compilation failed: $Evidence/compile-process.json"}
 '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.29"}}}' | Set-Content -LiteralPath (Join-Path $Evidence 'GuardProbe.runtimeconfig.json')
 @{probeSourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();guardSourceSha256=(Get-FileHash -LiteralPath $guard -Algorithm SHA256).Hash.ToLowerInvariant();policyTestsSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'GuardPolicyTests.cs') -Algorithm SHA256).Hash.ToLowerInvariant();assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant();compilerSha256=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant();ntdllSha256=(Get-FileHash -LiteralPath (Join-Path $env:SystemRoot 'System32/ntdll.dll') -Algorithm SHA256).Hash.ToLowerInvariant();policyTestsPassed=25;retainedMovePolicySha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'RetainedMovePolicy.cs') -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'build.json')
 $inputs=[ordered]@{};foreach($name in @('NativeNoDelete.cs','GuardProbe.cs','GuardPolicyTests.cs','RetainedMovePolicy.cs','RetainedMoveTests.cs','FixtureRetentionTests.cs','build.ps1','verify.ps1')){$inputs[$name]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash.ToLowerInvariant()};$inputs|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Evidence 'qualification-inputs.json')
 Write-Host "Built guarded probe in $Evidence"
} finally {$env:TEMP=$oldTemp;$env:TMP=$oldTmp}
