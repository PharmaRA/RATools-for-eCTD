[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('test', 'build')][string]$Task,
    [ValidateSet('frontend', 'linux-platform', 'linux-resources', 'backend-browser', 'postgres-platform', 'backend-pg-browser')][string]$Scope = 'frontend',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Machine = 'insta01',
    [ValidateSet('/data/home/ratools-remote', '/tmp/ratools-remote', IgnoreCase = $false)][string]$RemoteRoot = '/data/home/ratools-remote',
    [string]$CliPath,
    [string[]]$TestFiles = @('src/ErrorBoundary.test.tsx', 'src/pages/SequenceWorkspacePage.validation.test.tsx'),
    [string[]]$LintFiles = @(),
    [switch]$BrowserProbe,
    [ValidateRange(1, 180)][int]$WaitMinutes = 30,
    [string]$Resume
)
$ErrorActionPreference = 'Stop'
if ($BrowserProbe -and $Task -ne 'build') { throw 'BrowserProbe requires -Task build so it uses the current production output.' }
if ($Scope -ne 'frontend' -and ($BrowserProbe -or $LintFiles.Count)) { throw 'BrowserProbe and LintFiles require frontend scope.' }
$repo = Split-Path -Parent $PSScriptRoot
# An already-running parent application may not inherit a newly persisted user
# variable. Pass only this named variable to children, never print or serialize it.
if (-not $env:INSTABOX_API_KEY -and $IsWindows) {
    $env:INSTABOX_API_KEY = [Environment]::GetEnvironmentVariable('INSTABOX_API_KEY', 'User')
}
if (-not $CliPath) {
    $command = Get-Command instabox -ErrorAction SilentlyContinue
    $CliPath = if ($command) { $command.Source } else {
        Join-Path $repo '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe'
    }
}
if (-not (Test-Path -LiteralPath $CliPath -PathType Leaf)) { throw 'Instabox CLI missing. Supply -CliPath or install instabox in PATH.' }

function Invoke-Instabox([string[]]$Arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $CliPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['INSTABOX_NO_UPDATE_CHECK'] = '1'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $result = @{ Code = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() }
    # CLI errors are classified by exit code, never dump authentication/account output.
    $null = $stderr.GetAwaiter().GetResult()
    $process.Dispose()
    return $result
}

function Require-Transport($Result, [string]$Operation) {
    if ($Result.Code -eq 3) { throw "$Operation failed: Instabox authentication unavailable (exit 3). Run instabox login or set INSTABOX_API_KEY; no local fallback." }
    if ($Result.Code -ne 0) { throw "$Operation failed: remote transport/CLI exit $($Result.Code); no local fallback." }
}

function Resolve-CheckScope([string]$Value) {
    if (-not $Value -or $Value -eq 'frontend only') { return 'frontend' }
    return $Value
}

# Probe without querying account details or inspecting credential storage.
$probe = Invoke-Instabox @('exec', $Machine, '--json', '--timeout', '30', '--', 'true')
Require-Transport $probe 'Authentication/connectivity probe'

if ($Resume) {
    $resumePath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Resume)) { $Resume } else { Join-Path $repo $Resume }))
    $evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts/remote-check')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resumePath.StartsWith($evidenceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Resume must name a run.json under .artifacts/remote-check.' }
    $state = Get-Content -LiteralPath $resumePath -Raw | ConvertFrom-Json
    if ($state.machine -ne $Machine -or $state.task -ne $Task -or (Resolve-CheckScope $state.scope) -ne $Scope -or $state.jobId -notmatch '^[a-zA-Z0-9_-]+$' -or
        $state.remote -cnotmatch '^/(data/home|tmp)/ratools-remote/[a-zA-Z0-9_-]+$' -or
        $state.sourceSha256 -notmatch '^[a-f0-9]{64}$') { throw 'Resume state does not match the requested job.' }
    $evidence = Split-Path -Parent $resumePath
} else {
    $runId = (Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N')
    $evidence = Join-Path $repo ".artifacts/remote-check/$runId"
    $remote = "$RemoteRoot/$runId"
    $null = New-Item -ItemType Directory -Path $evidence

    # Explicit frontend source/config rules. No .env, data, backups, bin, obj,
    # node_modules, local CLI/auth state, or backend artifacts are synchronized.
    $files = [Collections.Generic.List[string]]::new()
    if ($Scope -in @('frontend', 'backend-browser', 'backend-pg-browser', 'linux-resources')) {
    foreach ($name in @('package.json', 'package-lock.json', 'index.html', 'bundle-budget.json',
        'vite.config.ts', 'eslint.config.js', 'postcss.config.js', 'tailwind.config.js',
        'tsconfig.json', 'tsconfig.app.json', 'tsconfig.node.json', 'scripts/check-bundle-budget.mjs')) {
        $files.Add("frontend/$name")
    }
    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $repo 'frontend/src') -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($repo, $item.FullName).Replace('\', '/')
        if ($item.Extension -in @('.ts', '.tsx', '.css', '.json', '.svg') -and
            $relative -notmatch '(^|/)(bin|obj|node_modules|backups|\.git|\.artifacts|credentials|secrets|data)(/|$)' -and
            $relative -notmatch '(?i)(^|/)(\.env[^/]*|[^/]*(secret|credential|token|private[-_]?key)[^/]*)$') {
            $files.Add($relative)
        }
    }
    foreach ($name in @('job.py', 'manager-launch.py', 'manager-launch.test.py', 'install-lock.py', 'no-delete.py', 'no-delete.mjs', 'test.mjs', 'browser-finalization.mjs', 'browser-process.mjs', 'browser-process.test.mjs')) {
        $files.Add("scripts/remote-check/$name")
    }
    if ($Scope -in @('backend-browser', 'backend-pg-browser', 'linux-resources')) {
        foreach ($name in @('global.json', 'Directory.Build.props', 'VERSION',
            'scripts/remote-check/backend-build.py', 'scripts/remote-check/browser-backend.mjs',
            'scripts/remote-check/BrowserStartup.cs', 'scripts/remote-check/CaptureRecycleProbe.cs',
            'scripts/remote-check/ProcessTreeProbe.cs', 'scripts/remote-check/LinuxResourceProbe.cs', 'scripts/remote-check/verify-pdf-download.py',
            'src/RATools.DatabaseMigrator/Program.cs', 'src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj')) { $files.Add($name) }
        if ($Scope -eq 'backend-pg-browser') {
            foreach ($name in @('postgres-platform.py', 'postgres-packages.json', 'RecyclePostgres.c')) { $files.Add("scripts/remote-check/$name") }
        }
        foreach ($layer in @('Domain', 'Application', 'Infrastructure', 'Api')) {
            $projectDirectory = Join-Path $repo "src/RATools.$layer"
            $projectFile = Join-Path $projectDirectory "RATools.$layer.csproj"
            $files.Add("src/RATools.$layer/RATools.$layer.csproj")
            foreach ($item in Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Filter '*.cs') {
                $relative = [IO.Path]::GetRelativePath($repo, $item.FullName).Replace('\', '/')
                if ($relative -notmatch '/(bin|obj|App_Data)/') { $files.Add($relative) }
            }
            [xml]$project = Get-Content -LiteralPath $projectFile -Raw
            foreach ($resource in $project.SelectNodes('//EmbeddedResource')) {
                $path = [IO.Path]::GetFullPath((Join-Path $projectDirectory $resource.Include))
                if (-not $path.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Embedded resource is outside workspace.' }
                $files.Add([IO.Path]::GetRelativePath($repo, $path).Replace('\', '/'))
            }
        }
    }
    foreach ($testFile in $TestFiles) {
        if ($testFile -notmatch '^src/[a-zA-Z0-9_./-]+\.test\.tsx?$' -or $testFile.Contains('..') -or
            -not $files.Contains("frontend/$testFile")) { throw "Test is outside the synchronized frontend source rules: $testFile" }
    }
    foreach ($lintFile in $LintFiles) {
        if ($lintFile -notmatch '^(src/[a-zA-Z0-9_./-]+\.(ts|tsx)|vite\.config\.ts)$' -or $lintFile.Contains('..') -or
            -not $files.Contains("frontend/$lintFile")) { throw "Lint file is outside synchronized source rules: $lintFile" }
    }
    } elseif ($Scope -eq 'postgres-platform') {
        if ($Task -ne 'test') { throw 'postgres-platform currently supports test only.' }
        foreach ($name in @('job.py', 'no-delete.py', 'postgres-platform.py', 'postgres-packages.json', 'RecyclePostgres.c')) {
            $files.Add("scripts/remote-check/$name")
        }
    } else {
        foreach ($name in @('global.json', 'src/RATools.Infrastructure/RATools.Infrastructure.csproj',
            'src/RATools.Infrastructure/DocumentProcessing/PdfToolProcessBudget.cs',
            'src/RATools.Infrastructure/DocumentProcessing/PdfContentProcess.cs',
            'src/RATools.Infrastructure/Security/LocalOnlyDeploymentValidator.cs',
            'src/RATools.Infrastructure/Security/DeploymentOptions.cs',
            'scripts/remote-check/job.py', 'scripts/remote-check/no-delete.py',
            'scripts/remote-check/linux-platform.py', 'scripts/remote-check/LinuxPlatform.cs')) { $files.Add($name) }
    }
    $manifest = @()
    $archivePath = Join-Path $evidence 'source.zip'
    $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in ($files | Sort-Object -Unique)) {
            $absolute = Join-Path $repo $relative
            # Refuse links, including ancestor directory junctions, before reading.
            $candidate = Get-Item -LiteralPath $absolute -Force
            while ($candidate.FullName -ne $repo) {
                if ($candidate.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked source refused: $relative" }
                $candidate = Get-Item -LiteralPath (Split-Path -Parent $candidate.FullName) -Force
            }
            $bytes = [IO.File]::ReadAllBytes($absolute)
            $manifest += @{ path = $relative; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
            $entry = $archive.CreateEntry($relative)
            $stream = $entry.Open()
            try { $stream.Write($bytes) } finally { $stream.Dispose() }
        }
        $config = @{ task = $Task; scope = $Scope; testFiles = $TestFiles; lintFiles = $LintFiles; browserProbe = [bool]$BrowserProbe; sourceHead = (& git -C $repo rev-parse HEAD) }
        $entry = $archive.CreateEntry('job-config.json')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write(($config | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'source-manifest.json')
    $digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $upload = Invoke-Instabox @('cp', '-p', $archivePath, "${Machine}:$remote/source.zip", '--json')
    Require-Transport $upload 'Source upload'
    $bootstrap = "import hashlib,zipfile; p='$remote/source.zip'; assert hashlib.sha256(open(p,'rb').read()).hexdigest()=='$digest'; zipfile.ZipFile(p).extractall('$remote')"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($bootstrap))
    $unpack = Invoke-Instabox @('exec', $Machine, '--json', '--', "python3 -c 'import base64; exec(base64.b64decode(`"$encoded`"))'")
    Require-Transport $unpack 'Verified source extraction'
    $jobCommand = "python3 scripts/remote-check/no-delete.py python3 scripts/remote-check/job.py $digest"
    if ($Scope -in @('linux-resources', 'backend-browser', 'backend-pg-browser')) {
        $jobCommand = "python3 scripts/remote-check/no-delete.py python3 scripts/remote-check/manager-launch.py $digest"
    }
    $launch = Invoke-Instabox @('exec', $Machine, '--background', '--json', '--cwd', $remote, '--', $jobCommand)
    Require-Transport $launch 'Background job launch'
    $job = $launch.Output | ConvertFrom-Json
    $state = @{ machine = $Machine; task = $Task; scope = $Scope; remote = $remote; jobId = $job.jobId; sourceSha256 = $digest; sourceHead = $config.sourceHead;
        command = $jobCommand; entrySha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'run.json')
}

Write-Host "Remote $Task job $($state.jobId); source $($state.sourceHead); archive SHA256 $($state.sourceSha256)"
Write-Host "Command: $(if ($state.command) { $state.command } else { 'python3 scripts/remote-check/no-delete.py python3 scripts/remote-check/job.py ' + $state.sourceSha256 })"
Write-Host "Evidence: $evidence; remote: $($state.remote)"
$deadline = [DateTime]::UtcNow.AddMinutes($WaitMinutes)
do {
    $wait = Invoke-Instabox @('jobs', 'wait', $Machine, $state.jobId, '--timeout', '20s', '--json')
    $finished = $null
    try { $finished = $wait.Output | ConvertFrom-Json } catch { }
    $hasFinalRecord = $null -ne $finished -and $finished.jobId -eq $state.jobId -and $null -ne $finished.exitCode
    if ($wait.Code -eq 124 -and -not $hasFinalRecord) {
        Write-Host 'Remote job still running; waiting for its final status.'
    } else {
        # A job can itself exit 3 or 124: a matching final record takes precedence.
        if (-not $hasFinalRecord) { throw "Remote job state unavailable/mismatched (CLI exit $($wait.Code)); resume using run.json." }
        $finished | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'job-result.json')
        Write-Host "Remote job ended with exit $($finished.exitCode); retrieving evidence."
        $downloadDirectory = $evidence
        if (Test-Path -LiteralPath (Join-Path $downloadDirectory 'results')) {
            $downloadDirectory = Join-Path $evidence ('retrieval-' + [guid]::NewGuid().ToString('N'))
            $null = New-Item -ItemType Directory -Path $downloadDirectory
        }
        $pull = Invoke-Instabox @('cp', '-r', "${Machine}:$($state.remote)/results", $downloadDirectory, '--json')
        Require-Transport $pull 'Result download'
        $summary = Get-Content -LiteralPath (Join-Path $downloadDirectory 'results/summary.json') -Raw | ConvertFrom-Json
        if ($summary.task -ne $state.task -or $summary.sourceHead -ne $state.sourceHead -or
            (Resolve-CheckScope $summary.scope) -ne $Scope -or
            $summary.sourceSha256 -ne $state.sourceSha256 -or $summary.exitCode -ne $finished.exitCode -or
            ($finished.exitCode -eq 0 -and $summary.category -ne 'passed')) { throw 'Downloaded summary does not bind to this job; success is not established.' }
        Write-Host "Final remote exit: $($finished.exitCode); category: $($summary.category); report: $downloadDirectory/results/summary.json"
        if (Test-Path -LiteralPath (Join-Path $downloadDirectory 'results/tests.json')) {
            $tests = Get-Content -LiteralPath (Join-Path $downloadDirectory 'results/tests.json') -Raw | ConvertFrom-Json
            Write-Host "Tests: $($tests.numPassedTests) passed / $($tests.numFailedTests) failed / $($tests.numPendingTests) pending"
        }
        if (Test-Path -LiteralPath (Join-Path $downloadDirectory 'results/linux-tests.json')) {
            $tests = Get-Content -LiteralPath (Join-Path $downloadDirectory 'results/linux-tests.json') -Raw | ConvertFrom-Json
            Write-Host "Linux tests: $($tests.passed) passed / $($tests.failed) failed / $($tests.skipped) skipped"
        }
        exit ([int]$finished.exitCode)
    }
} while ([DateTime]::UtcNow -lt $deadline)
Write-Host "Wait timeout; job remains running. Resume: pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task $Task -Scope $Scope -Machine $Machine -Resume '$evidence/run.json'"
exit 124
