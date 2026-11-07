[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence, [string]$Driver = 'browser-probe.mjs')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts/local-check')) + [IO.Path]::DirectorySeparatorChar
$Evidence = [IO.Path]::GetFullPath($Evidence)
if (-not $Evidence.StartsWith($evidenceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must remain within local-check outputs.' }
if ($Driver -notin @('browser-probe.mjs','input-browser.mjs')) { throw 'Unreviewed browser driver.' }
$guardRoot = Join-Path $Evidence 'browser-data'
New-Item -ItemType Directory -Path $guardRoot | Out-Null
foreach ($name in @('profile','temp','cache','appdata','localappdata','downloads')) { New-Item -ItemType Directory -Path (Join-Path $guardRoot $name) | Out-Null }
$probe = Join-Path $guardRoot 'retention-probe.txt'
Set-Content -LiteralPath $probe -Value 'retained permission probe'
$original = Get-Acl -LiteralPath $guardRoot
$backup = Join-Path $repo 'backups/qa-07-local-20261107/browser-guard'
New-Item -ItemType Directory -Path $backup -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $backup ((Split-Path -Leaf $Evidence) + '.acl')), $original.GetSecurityDescriptorBinaryForm())
$acl = Get-Acl -LiteralPath $guardRoot
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$rule = [Security.AccessControl.FileSystemAccessRule]::new($sid,
    ([Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles),
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $guardRoot -AclObject $acl
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BrowserDeleteAccessProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    public static int Check(string path) {
        // Request DELETE access only. No disposition/delete flag or deletion API is used.
        var handle=CreateFileW(path,0x10000,7,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle==new IntPtr(-1))return Marshal.GetLastWin32Error();
        CloseHandle(handle);return 0;
    }
}
'@
$denied = [BrowserDeleteAccessProbe]::Check($probe)
if ($denied -ne 5) { throw "Delete-access guard did not fail closed: $denied" }
$browserExecutable = Join-Path $repo '.artifacts/tools/chrome-headless-shell-157.0.8103.38/chrome-headless-shell-win64/chrome-headless-shell.exe'
@{ deleteAccessDenied=$true; inheritedAcl=$true; physicalDeleteAttempted=$false; guardRoot=$guardRoot; nativeError=$denied;
    executableSha256=(Get-FileHash -LiteralPath $browserExecutable -Algorithm SHA256).Hash.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'browser-guard.json')
$guard = [Uri]::new((Join-Path $repo 'scripts/remote-check/no-delete.mjs')).AbsoluteUri
Add-Type -Path (Join-Path $PSScriptRoot 'BrowserJob.cs')
$node = (Get-Command node).Source
$arguments = '--import "' + $guard + '" "' + (Join-Path $PSScriptRoot $Driver) + '" "' + $Evidence + '"'
$code = [BrowserJob]::Run($node, $arguments, $repo, (Join-Path $Evidence 'browser-driver.log'), (Join-Path $Evidence 'browser-process-tree.json'))
$resultPath = Join-Path $Evidence 'browser-result.json'
if (Test-Path -LiteralPath $resultPath) {
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -AsHashtable
    $tree = Get-Content -LiteralPath (Join-Path $Evidence 'browser-process-tree.json') -Raw | ConvertFrom-Json
    $result['processTreeStopped'] = $tree.processTreeStopped
    if ($code -ne 0 -or -not $tree.processTreeStopped) { $result['exitCode'] = 1 }
    $result | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $resultPath
}
exit $code
