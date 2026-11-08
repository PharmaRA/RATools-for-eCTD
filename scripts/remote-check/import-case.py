"""Bounded Linux execution of existing case-sensitive hash cache test methods."""
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess

root = Path.cwd()
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
result = {'exitCode': 1, 'scope': 'Linux existing hash cache test methods; not full suite'}
try:
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise RuntimeError('Linux x86_64 required')
    if 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Inherited no-delete seccomp required')
    manifest = json.loads((root / 'source-manifest.json').read_text())
    required = {'src/RATools.Application/Applications/ImportFileHashCache.cs',
                'tests/RATools.Tests/Applications/ImportFileHashCacheTests.cs',
                'scripts/remote-check/ImportCaseProbe.cs', 'scripts/remote-check/import-case.py',
                'scripts/remote-check/no-delete.py', 'xunit.assert.dll',
                'xunit.core.dll', 'xunit.abstractions.dll'}
    if set(manifest) != required:
        raise RuntimeError('Source manifest must contain exactly the eight required inputs')
    for relative, digest in manifest.items():
        path = root / relative
        if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Uploaded source differs: ' + relative)
    for directory in ('temp', 'cli'):
        (root / directory).mkdir()
    env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_NOLOGO': '1',
           'DOTNET_EnableDiagnostics': '0', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
           'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1', 'DOTNET_CLI_HOME': str(root / 'cli'),
           'TMPDIR': str(root / 'temp'), 'TMP': str(root / 'temp'), 'TEMP': str(root / 'temp')}
    version = subprocess.check_output([str(sdk / 'dotnet'), '--version'], env=env, text=True, timeout=20).strip()
    if version != '8.0.423':
        raise RuntimeError('Unexpected SDK version')
    result['sdk'] = version
    refs = sorted((sdk / 'packs/Microsoft.NETCore.App.Ref/8.0.29/ref/net8.0').glob('*.dll'))
    if not refs:
        raise RuntimeError('Missing reference pack')
    (root / 'GlobalUsings.cs').write_text('\n'.join('global using ' + value + ';' for value in
        ['System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Threading', 'System.Threading.Tasks', 'Xunit']))
    sources = ['src/RATools.Application/Applications/ImportFileHashCache.cs',
               'tests/RATools.Tests/Applications/ImportFileHashCacheTests.cs',
               'scripts/remote-check/ImportCaseProbe.cs', 'GlobalUsings.cs']
    command = [str(sdk / 'dotnet'), str(sdk / 'sdk/8.0.423/Roslyn/bincore/csc.dll'),
               '-nologo', '-target:exe', '-langversion:12', '-nullable:enable', '-warnaserror+',
               '-nowarn:1701,1702', '-out:ImportCaseProbe.dll']  # Microsoft.NET.Sdk.CSharp.props defaults.
    command += ['-r:' + str(path) for path in refs]
    command += ['-r:' + name for name in ('xunit.assert.dll', 'xunit.core.dll', 'xunit.abstractions.dll')]
    command += sources
    with (root / 'compile.log').open('x') as log:
        build = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=60)
    result['buildExitCode'] = build.returncode
    if build.returncode:
        raise RuntimeError('Compilation failed; see compile.log')
    (root / 'ImportCaseProbe.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.29'}}}))
    with (root / 'test.log').open('x') as log:
        run = subprocess.run([str(sdk / 'dotnet'), 'ImportCaseProbe.dll', 'tests.json'],
                             env=env, stdout=log, stderr=subprocess.STDOUT, timeout=60)
    result['testExitCode'] = run.returncode
    report = json.loads((root / 'tests.json').read_text())
    result['tests'] = report
    if run.returncode or (report['total'], report['passed'], report['failed'], report['skipped']) != (3, 3, 0, 0):
        raise RuntimeError('Linux source test methods did not all pass')
    inputs = sorted((root / 'temp/ratools-import-case-evidence').glob('*/*.pdf'))
    if len(inputs) != 2 or {path.name for path in inputs} != {'a.pdf', 'A.pdf'}:
        raise RuntimeError('Expected two distinct case-sensitive filesystem paths')
    result['retainedInputs'] = [{'path': str(path.relative_to(root)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in inputs]
    result['sourceManifestSha256'] = hashlib.sha256((root / 'source-manifest.json').read_bytes()).hexdigest()
    result['exitCode'] = 0
except Exception as error:
    result['error'] = str(error)
finally:
    (root / 'summary.json').write_text(json.dumps(result, indent=2))
print(json.dumps(result))
raise SystemExit(result['exitCode'])
