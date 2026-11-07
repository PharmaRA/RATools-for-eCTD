[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Evidence = [IO.Path]::GetFullPath($Evidence)
$allowed = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts/local-check'))
if([IO.Path]::GetDirectoryName($Evidence) -ne $allowed){throw 'Frontend evidence must be a direct local-check child.'}
function Assert-NoLinks([string]$Path) {
    for($ancestor=[IO.Path]::GetFullPath($Path);$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Task paths must not traverse links.'}
    }
}
$summary=@{task='frontend-all';exitCode=1;steps=@()}
try {
    Assert-NoLinks $Evidence
    $backup=Join-Path $repo 'backups/qa-07-local-20261107/frontend-all'
    Assert-NoLinks $backup; New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $acl=Get-Acl -LiteralPath $Evidence
    $stream=[IO.FileStream]::new((Join-Path $backup ((Split-Path -Leaf $Evidence)+'.acl')),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=$acl.GetSecurityDescriptorBinaryForm();$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,([Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles),([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -LiteralPath $Evidence -AclObject $acl
    $env:TEMP=Join-Path $Evidence 'temp';$env:TMP=$env:TEMP;New-Item -ItemType Directory -Path $env:TEMP | Out-Null
    $proof=Join-Path $Evidence 'guard-proof.txt';Set-Content -LiteralPath $proof -Value 'retained frontend guard proof'
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FrontendDeleteAccessProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
 public static int Check(string path){var h=CreateFileW(path,0x10000,7,IntPtr.Zero,3,0,IntPtr.Zero);if(h==new IntPtr(-1))return Marshal.GetLastWin32Error();CloseHandle(h);return 0;}
}
'@
    if([FrontendDeleteAccessProbe]::Check($proof) -ne 5){throw 'Native delete access guard did not refuse access.'}
    $summary.nativeDeleteAccessDenied=$true
    Add-Type -Path (Join-Path $PSScriptRoot 'BrowserJob.cs')
    $node=(Get-Command node).Source
    $guard=[Uri]::new((Join-Path $repo 'scripts/remote-check/no-delete.mjs')).AbsoluteUri
    foreach($phase in @('tests','build')) {
        $arguments='--import "'+$guard+'" "'+(Join-Path $PSScriptRoot 'frontend-all.mjs')+'" "'+$Evidence+'" '+$phase
        $code=[BrowserJob]::Run($node,$arguments,$repo,(Join-Path $Evidence ($phase+'.log')),(Join-Path $Evidence ($phase+'.process-tree.json')))
        $summary.steps+=@{phase=$phase;exitCode=$code}
        Write-Host "Frontend $phase exit $code"
        if($code -ne 0){throw "Frontend $phase failed; see retained log and summary."}
        $result=Get-Content -LiteralPath (Join-Path $Evidence ($phase+'-summary.json')) -Raw | ConvertFrom-Json
        if($result.exitCode -ne 0){throw "Frontend $phase result did not pass."}
    }
    $summary.exitCode=0
} catch {$summary.error=$_.Exception.Message;Write-Host $summary.error}
finally {$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Evidence 'summary.json')}
exit $summary.exitCode
