[CmdletBinding()]
param([Parameter(Mandatory)][string]$Evidence, [switch]$RunSelectedTests, [ValidateSet('core','contracts','import','nodes','package')][string]$TestGroup = 'core')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Evidence = [IO.Path]::GetFullPath($Evidence)
$allowed = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts/local-check')) + [IO.Path]::DirectorySeparatorChar
if (-not $Evidence.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence outside local-check.' }
$sdk = Join-Path $repo '.artifacts/tools/dotnet-8.0.423'
$dotnet = Join-Path $sdk 'dotnet.exe'
$env:DOTNET_ROOT = $sdk
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:MSBUILDPRESERVETOOLTEMPFILES = '1'
$summary = @{task='msbuild';sourceHead=(& git -C $repo rev-parse HEAD);exitCode=1;steps=@();scope='Windows actual MSBuild Build targets, pinned cached restore assets, fail-closed cleanup tasks and deny-delete output ACL; not runtime tests'}
if($RunSelectedTests) { $summary.task='msbuild-tests'; $summary.scope='Windows actual MSBuild plus real VSTest for 11 explicit classes/156 expected cases; cached restore assets; not full suite, fresh restore or cross-platform qualification' }
if($RunSelectedTests -and $TestGroup -eq 'contracts') { $summary.task='msbuild-contracts'; $summary.scope='Windows actual MSBuild plus real VSTest for 4 explicit classes/34 expected cases; cached restore assets; not PostgreSQL, full suite, fresh restore or Formal qualification' }
if($RunSelectedTests -and $TestGroup -eq 'import') { $summary.task='msbuild-import'; $summary.scope='Windows actual MSBuild plus real VSTest for 3 explicit classes/42 expected import and lifecycle cases; InMemory and EF InMemory provider; not PostgreSQL, full suite or Formal qualification' }
if($RunSelectedTests -and $TestGroup -eq 'nodes') { $summary.task='msbuild-nodes'; $summary.scope='Windows actual MSBuild plus real VSTest for 2 explicit classes/34 expected node publication and import cases; InMemory; not PostgreSQL, full suite or Formal qualification' }
if($RunSelectedTests -and $TestGroup -eq 'package') { $summary.task='msbuild-package'; $summary.scope='Windows actual MSBuild plus real VSTest for 6 explicit classes/189 expected independent package validation cases; retained fixture files; not PostgreSQL, full suite, fresh restore or Formal qualification' }
$inputs = @{}; $tools = @{}
function Fingerprint([string]$Path) { $inputs[[IO.Path]::GetRelativePath($repo,$Path).Replace('\','/')] = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
try {
    foreach($name in @('temp','obj','bin','cli')) { New-Item -ItemType Directory -Path (Join-Path $Evidence $name) | Out-Null }
    $env:TEMP = Join-Path $Evidence 'temp'; $env:TMP = $env:TEMP; $env:DOTNET_CLI_HOME = Join-Path $Evidence 'cli'
    foreach($path in @('scripts/local-check.ps1','scripts/local-check/MsbuildNoDelete.cs','scripts/local-check/msbuild.ps1','scripts/local-check/BrowserJob.cs','Directory.Build.props','.editorconfig','global.json','VERSION')) { Fingerprint (Join-Path $repo $path) }
    $version = & $dotnet --version
    if($version -ne '8.0.423') { throw 'Pinned SDK version differs from the reviewed toolchain' }
    $summary.sdk=$version
    foreach($path in @('dotnet.exe','sdk/8.0.423/MSBuild.dll','sdk/8.0.423/Microsoft.Build.dll','sdk/8.0.423/Microsoft.Build.Utilities.Core.dll','sdk/8.0.423/Microsoft.Build.Framework.dll','sdk/8.0.423/Roslyn/bincore/csc.dll','sdk/8.0.423/Microsoft.Common.CurrentVersion.targets','sdk/8.0.423/Roslyn/Microsoft.CSharp.Core.targets')) {
        $tools[$path]=(Get-FileHash -LiteralPath (Join-Path $sdk $path) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $compiler = Join-Path $sdk 'sdk/8.0.423/Roslyn/bincore/csc.dll'
    $taskDll = Join-Path $Evidence 'MsbuildNoDelete.dll'
    $references = @(Get-ChildItem -LiteralPath (Join-Path $sdk 'packs/Microsoft.NETCore.App.Ref/8.0.29/ref/net8.0') -File -Filter '*.dll' | ForEach-Object { '-r:' + $_.FullName })
    $references += '-r:' + (Join-Path $sdk 'sdk/8.0.423/Microsoft.Build.Framework.dll')
    $references += '-r:' + (Join-Path $sdk 'sdk/8.0.423/Microsoft.Build.Utilities.Core.dll')
    $arguments = @('-nologo','-target:library','-langversion:12','-nullable:enable','-warnaserror+',('-out:'+$taskDll)) + $references + (Join-Path $PSScriptRoot 'MsbuildNoDelete.cs')
    $response = Join-Path $Evidence 'guard.rsp'
    $arguments | ForEach-Object {'"'+$_+'"'} | Set-Content -LiteralPath $response
    & $dotnet $compiler ('@'+$response) *> (Join-Path $Evidence 'guard-compile.log')
    if($LASTEXITCODE -ne 0) { throw 'Guard compilation failed' }
    $targets = Join-Path $Evidence 'NoDelete.targets'
    @"
<Project>
  <UsingTask TaskName="Microsoft.Build.Tasks.Delete" AssemblyFile="$(Xml $taskDll)" Override="true" />
  <UsingTask TaskName="Microsoft.Build.Tasks.RemoveDir" AssemblyFile="$(Xml $taskDll)" Override="true" />
</Project>
"@ | Set-Content -LiteralPath $targets
    $props = Join-Path $Evidence 'Build.props'
    @"
<Project>
 <Import Project="$(Xml (Join-Path $repo 'Directory.Build.props'))" />
 <PropertyGroup>
  <BaseIntermediateOutputPath>$(Xml $Evidence)/obj/`$(MSBuildProjectName)/</BaseIntermediateOutputPath>
  <MSBuildProjectExtensionsPath>`$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
  <BaseOutputPath>$(Xml $Evidence)/bin/`$(MSBuildProjectName)/</BaseOutputPath>
  <DefaultItemExcludes>`$(DefaultItemExcludes);`$(MSBuildProjectDirectory)/obj/**;`$(MSBuildProjectDirectory)/bin/**</DefaultItemExcludes>
  <CustomAfterMicrosoftCommonTargets>$(Xml $targets)</CustomAfterMicrosoftCommonTargets>
 </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $props
    foreach($layer in @('Domain','Application','Infrastructure','Api','DatabaseMigrator','Tests')) {
        $name='RATools.'+$layer; $projectDirectory=if($layer -eq 'Tests'){Join-Path $repo "tests/$name"}else{Join-Path $repo "src/$name"}
        $project=Join-Path $projectDirectory "$name.csproj"; Fingerprint $project
        foreach($source in Get-ChildItem -LiteralPath (Split-Path -Parent $project) -Recurse -File -Filter '*.cs') {
            if($source.FullName -notmatch '[\\/](bin|obj|App_Data)[\\/]') { Fingerprint $source.FullName }
        }
        $obj=Join-Path $projectDirectory 'obj'; $destination=Join-Path $Evidence "obj/$name"
        New-Item -ItemType Directory -Path $destination | Out-Null
        foreach($file in @('project.assets.json',"$name.csproj.nuget.g.props","$name.csproj.nuget.g.targets")) {
            $source=Join-Path $obj $file; Fingerprint $source; Copy-Item -LiteralPath $source -Destination (Join-Path $destination $file)
        }
        $assets=Get-Content -LiteralPath (Join-Path $obj 'project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
        [xml]$projectXml=Get-Content -LiteralPath $project -Raw
        foreach($package in $projectXml.SelectNodes('//PackageReference')) {
            if(-not $assets.targets['net8.0'].ContainsKey($package.Include+'/'+$package.Version)) { throw "Stale restore assets: $name" }
        }
        foreach($resource in $projectXml.SelectNodes('//EmbeddedResource')) { Fingerprint ([IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $project) $resource.Include))) }
    }
    foreach($content in Get-ChildItem -LiteralPath (Join-Path $repo 'reference/dtd') -File -Filter '*.dtd') { Fingerprint $content.FullName }
    foreach($content in Get-ChildItem -LiteralPath (Join-Path $repo 'reference/eu-m1') -Recurse -File) { if($content.FullName -match '[\\/]util[\\/]') { Fingerprint $content.FullName } }
    foreach($fixtureSet in @('Publisher','CtdNodes','PackageValidation')) {
        foreach($content in Get-ChildItem -LiteralPath (Join-Path $repo "tests/RATools.Tests/Fixtures/$fixtureSet") -Recurse -File) { Fingerprint $content.FullName }
    }
    foreach($path in @('src/RATools.Api/openapi.v1.json','reference/publisher/contract-examples.json')) { Fingerprint (Join-Path $repo $path) }
    $proof=Join-Path $Evidence 'retained.txt'; Set-Content -LiteralPath $proof -Value 'retained MSBuild guard proof'
    $smoke=Join-Path $Evidence 'guard.proj'
    @"
<Project><Import Project="$(Xml $targets)" /><Target Name="Proof"><Delete Files="$(Xml $proof)" TreatErrorsAsWarnings="true" /></Target><Target Name="DirectoryProof"><RemoveDir Directories="$(Xml (Join-Path $Evidence 'retained-directory'))" /></Target></Project>
"@ | Set-Content -LiteralPath $smoke
    & $dotnet msbuild $smoke -target:Proof -nologo -verbosity:minimal *> (Join-Path $Evidence 'guard-proof.log')
    if($LASTEXITCODE -eq 0 -or -not (Test-Path -LiteralPath $proof) -or -not (Select-String -LiteralPath (Join-Path $Evidence 'guard-proof.log') -Pattern 'QA_NO_DELETE' -Quiet)) { throw 'MSBuild deletion task override did not fail closed' }
    $retainedDirectory=Join-Path $Evidence 'retained-directory'; New-Item -ItemType Directory -Path $retainedDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $retainedDirectory 'child.txt') -Value 'retained child'
    & $dotnet msbuild $smoke -target:DirectoryProof -nologo -verbosity:minimal *> (Join-Path $Evidence 'guard-directory-proof.log')
    if($LASTEXITCODE -eq 0 -or -not (Test-Path -LiteralPath (Join-Path $retainedDirectory 'child.txt')) -or -not (Select-String -LiteralPath (Join-Path $Evidence 'guard-directory-proof.log') -Pattern 'QA_NO_DELETE' -Quiet)) { throw 'MSBuild directory deletion override did not fail closed' }
    $summary.guardProof='Deletion request rejected; target retained; TreatErrorsAsWarnings cannot downgrade refusal'
    # Keep native task cleanup from deleting files outside the explicit MSBuild tasks.
    $acl=Get-Acl -LiteralPath $Evidence
    $backup=Join-Path $repo 'backups/qa-07-local-20261107/msbuild'
    for($ancestor=[IO.Path]::GetFullPath($backup);$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'ACL backup path must not traverse links.' }
    }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $aclStream=[IO.FileStream]::new((Join-Path $backup ((Split-Path -Leaf $Evidence)+'.acl')),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $descriptor=$acl.GetSecurityDescriptorBinaryForm(); $aclStream.Write($descriptor,0,$descriptor.Length) } finally { $aclStream.Dispose() }
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,([Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles),([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -LiteralPath $Evidence -AclObject $acl
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MsbuildDeleteAccessProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
 public static int Check(string path){var h=CreateFileW(path,0x10000,7,IntPtr.Zero,3,0,IntPtr.Zero);if(h==new IntPtr(-1))return Marshal.GetLastWin32Error();CloseHandle(h);return 0;}
}
'@
    if([MsbuildDeleteAccessProbe]::Check($proof) -ne 5) { throw 'Native delete-access guard failed' }
    $summary.nativeDeleteAccessDenied=$true
    Add-Type -Path (Join-Path $PSScriptRoot 'BrowserJob.cs')
    foreach($layer in @('Api','DatabaseMigrator','Tests')) {
        $project=if($layer -eq 'Tests'){Join-Path $repo 'tests/RATools.Tests/RATools.Tests.csproj'}else{Join-Path $repo "src/RATools.$layer/RATools.$layer.csproj"}
        $command='build "'+$project+'" --no-restore --configuration Release --nologo --verbosity minimal -warnaserror -maxcpucount:1 -nodeReuse:false -p:UseSharedCompilation=false -p:DirectoryBuildPropsPath="'+$props+'"'
        $code=[BrowserJob]::Run($dotnet,$command,$repo,(Join-Path $Evidence ($layer+'.build.log')),(Join-Path $Evidence ($layer+'.process-tree.json')))
        $summary.steps+=@{project=$layer;command=$command;exitCode=$code}
        Write-Host "MSBuild $layer exit $code"
        if($code -ne 0){throw "MSBuild $layer failed; see build log"}
    }
    if($RunSelectedTests) {
        $env:RATOOLS_TEST_REPOSITORY_ROOT=$repo
        Fingerprint (Join-Path $repo 'scripts/pdf_content_worker.py')
        Fingerprint (Join-Path $repo 'tests/RATools.Tests/Fixtures/RegionalImport/ema-national-eu-m1-1.4.xml')
        $selected=@{
            'RATools.Tests.Applications.RegionalImportCompleteTests'=24
            'RATools.Tests.Applications.RegionalImportAcceptanceTests'=3
            'RATools.Tests.Applications.ApplicationImportNodeTests'=16
            'RATools.Tests.RegionalMetadata.RegionalMetadataModelTests'=11
            'RATools.Tests.RegionalMetadata.RegionalMetadataEditingTests'=26
            'RATools.Tests.Publishing.EuRegional.ScopedEuRegionalWriterTests'=23
            'RATools.Tests.Publishing.UsRegional.ScopedUsRegionalWriterTests'=22
            'RATools.Tests.Publishing.EuRegional.ScopedEuPackageBindingTests'=18
            'RATools.Tests.Publishing.UsRegional.ScopedUsPackageBindingTests'=10
            'RATools.Tests.DocumentProcessing.PdfContentContractTests'=1
            'RATools.Tests.DocumentProcessing.PdfCombinedQualificationTests'=2
        }
        $expectedTotal = 156
        if($TestGroup -eq 'contracts') {
            Fingerprint (Join-Path $repo 'tests/RATools.Tests/Fixtures/RegionalQualification/p609-declared-matrix.json')
            $selected=@{
                'RATools.Tests.RegionalMetadata.RegionalMetadataIntegrationTests'=13
                'RATools.Tests.PackageValidation.DeclaredQualificationMatrixTests'=1
                'RATools.Tests.Standards.StandardsSnapshotIntegrationTests'=13
                'RATools.Tests.Validation.PublishReadinessServiceTests'=7
            }
            $expectedTotal = 34
        }
        if($TestGroup -eq 'import') {
            $selected=@{
                'RATools.Tests.Applications.ApplicationImportRegionalTests'=14
                'RATools.Tests.Applications.ApplicationImportDeleteTests'=16
                'RATools.Tests.Publishing.LifecycleReferenceTests'=12
            }
            $expectedTotal = 42
        }
        if($TestGroup -eq 'nodes') {
            $selected=@{
                'RATools.Tests.Publishing.NodePublishingTests'=18
                'RATools.Tests.Applications.ApplicationImportNodeTests'=16
            }
            $expectedTotal = 34
        }
        if($TestGroup -eq 'package') {
            $selected=@{
                'RATools.Tests.PackageValidation.PackageInputReaderTests'=47
                'RATools.Tests.PackageValidation.PackageFileInspectorTests'=28
                'RATools.Tests.PackageValidation.PackageLifecycleInspectorTests'=49
                'RATools.Tests.PackageValidation.PackageQualificationTests'=14
                'RATools.Tests.PackageValidation.PackageValidationServiceTests'=9
                'RATools.Tests.PackageValidation.PackageXmlInspectorTests'=42
            }
            $expectedTotal = 189
            foreach($path in @('reference/publisher/contract-examples.json','reference/publisher/package-rules-v1.json','reference/publisher/package-rules-v1.sha256')) { Fingerprint (Join-Path $repo $path) }
        }
        # This bounded test-source preflight supplements the output ACL: fixtures
        # also own retained workspace files outside the build output tree.
        $selectedSources = @($selected.Keys | ForEach-Object { Join-Path $repo ('tests/RATools.Tests/' + $_.Substring('RATools.Tests.'.Length).Replace('.','/') + '.cs') })
        $selectedSources += Join-Path $repo 'tests/RATools.Tests/Applications/NodeImportWorkspace.cs'
        if($TestGroup -eq 'package') {
            $selectedSources += Join-Path $repo 'tests/RATools.Tests/PackageValidation/RetainedPackageTestFiles.cs'
            $selectedSources += Join-Path $repo 'tests/RATools.Tests/PackageValidation/PackageRuleCatalogTests.cs'
        }
        if($TestGroup -in @('import','nodes')) {
            foreach($path in @('tests/RATools.Tests/TestDoubles/EctdWorkspaceFixture.cs','tests/RATools.Tests/TestDoubles/PackageTestServices.cs','tests/RATools.Tests/Applications/ImportTestServices.cs','tests/RATools.Tests/Applications/RegionalImportAcceptanceTests.cs')) {
                $selectedSources += Join-Path $repo $path
            }
        }
        foreach($source in $selectedSources) {
            if(Select-String -LiteralPath $source -Pattern '\b(File|Directory)\s*\.\s*Delete\s*\(|FileOptions\.DeleteOnClose' -Quiet) {
                throw 'Selected fixture contains physical deletion; review and replace with retention before execution.'
            }
        }
        $filter=($selected.Keys | Sort-Object | ForEach-Object { 'FullyQualifiedName~'+$_+'.' }) -join '|'
        $assembly=Join-Path $Evidence 'bin/RATools.Tests/Release/net8.0/RATools.Tests.dll'
        $results=Join-Path $Evidence 'test-results'; New-Item -ItemType Directory -Path $results | Out-Null
        $settings=Join-Path $Evidence 'selected.runsettings'
        '<RunSettings><RunConfiguration><MaxCpuCount>1</MaxCpuCount><DisableAppDomain>true</DisableAppDomain></RunConfiguration></RunSettings>' | Set-Content -LiteralPath $settings
        $command='vstest "'+$assembly+'" /Settings:"'+$settings+'" /TestCaseFilter:"'+$filter+'" /Logger:"trx;LogFileName=selected.trx" /ResultsDirectory:"'+$results+'"'
        $code=[BrowserJob]::Run($dotnet,$command,$repo,(Join-Path $Evidence 'selected-tests.log'),(Join-Path $Evidence 'selected-tests.process-tree.json'))
        $summary.steps+=@{name='selected-vstest';command=$command;exitCode=$code}
        $selected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'selected-expected.json')
        Write-Host "VSTest selected exit $code"
        [xml]$trx=Get-Content -LiteralPath (Join-Path $results 'selected.trx') -Raw
        $summary.tests=@{total=[int]$trx.TestRun.ResultSummary.Counters.total;passed=[int]$trx.TestRun.ResultSummary.Counters.passed;failed=[int]$trx.TestRun.ResultSummary.Counters.failed;notExecuted=[int]$trx.TestRun.ResultSummary.Counters.notExecuted}
        foreach($class in $selected.Keys){
            $ids=@($trx.TestRun.TestDefinitions.UnitTest | Where-Object { $_.TestMethod.className.Split(',')[0] -eq $class } | ForEach-Object {$_.id})
            $cases=@($trx.TestRun.Results.UnitTestResult | Where-Object {$_.testId -in $ids})
            if($cases.Count -ne $selected[$class]){throw "Unexpected executed case count for $class : $($cases.Count)"}
        }
        if($code -ne 0 -or $summary.tests.failed -ne 0 -or $summary.tests.notExecuted -ne 0 -or $summary.tests.passed -ne $expectedTotal){throw 'Selected runtime checks failed; see TRX and selected-tests.log'}
    }
    $summary.exitCode=0
} catch { $summary.error=$_.Exception.Message; Write-Host $summary.error }
finally {
    $inputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Evidence 'source-manifest.json')
    $tools | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'tool-fingerprints.json')
    $summary.sourceManifestSha256=(Get-FileHash -LiteralPath (Join-Path $Evidence 'source-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Evidence 'summary.json')
}
exit $summary.exitCode
