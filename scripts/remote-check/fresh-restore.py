"""Actual NuGet restore into empty Linux task caches with retained cleanup."""
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import time

root = Path.cwd()
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
summary = {'exitCode': 1, 'steps': [], 'scope': 'Fresh NuGet resolution and packages; no backend build/runtime claim'}


def live_group(group):
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(fields[2]) == group and fields[0] not in {'Z', 'X'}:
                found.append(int(entry.name))
        except (FileNotFoundError, ProcessLookupError):
            pass
    return found


def run(name, command, env, timeout):
    with (root / (name + '.log')).open('x') as log:
        process = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        timed_out = False
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=10)
            deadline = time.monotonic() + 5
            remaining = live_group(process.pid)
            while remaining and time.monotonic() < deadline:
                time.sleep(.05)
                remaining = live_group(process.pid)
            receipt = {'name': name, 'command': command, 'exitCode': process.returncode,
                       'timedOut': timed_out, 'remainingProcesses': remaining}
            summary['steps'].append(receipt)
    if timed_out or remaining or process.returncode:
        raise RuntimeError(name + ' failed or did not stop completely')


try:
    if 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Launch through the project no-delete guard')
    for part in ('temp', 'cli', 'packages', 'http', 'scratch', 'plugins', 'recycle'):
        (root / part).mkdir()
    env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_CLI_HOME': str(root / 'cli'),
           'PATH': str(sdk) + ':' + os.environ['PATH'], 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
           'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1', 'DOTNET_EnableDiagnostics': '0',
           'TMPDIR': str(root / 'temp'), 'TMP': str(root / 'temp'), 'TEMP': str(root / 'temp'),
           'NUGET_PACKAGES': str(root / 'packages'), 'NUGET_HTTP_CACHE_PATH': str(root / 'http'),
           'NUGET_SCRATCH': str(root / 'scratch'), 'NUGET_PLUGINS_CACHE_PATH': str(root / 'plugins'),
           'MSBUILDPRESERVETOOLTEMPFILES': '1', 'MSBUILDDISABLENODEREUSE': '1'}
    summary['initialPackageEntries'] = len(list((root / 'packages').iterdir()))
    run('shim-compile', ['cc', '-shared', '-fPIC', '-std=c11', '-Wall', '-Wextra', '-Werror',
        'scripts/remote-check/RecyclePostgres.c', '-o', 'recycle.so'], env, 30)
    env.update(LD_PRELOAD=str(root / 'recycle.so'), QA07_PG_ROOT=str(root), QA07_PG_RECYCLE=str(root / 'recycle'))
    (root / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    for layer, relative in [('Api', 'src/RATools.Api/RATools.Api.csproj'),
                            ('DatabaseMigrator', 'src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj'),
                            ('Tests', 'tests/RATools.Tests/RATools.Tests.csproj')]:
        command = [str(sdk / 'dotnet'), 'restore', relative, '--configfile', str(root / 'NuGet.Config'),
                   '--packages', str(root / 'packages'), '--force', '--no-cache', '--disable-parallel',
                   '--verbosity', 'minimal', '-maxcpucount:1', '-nodeReuse:false',
                   '-p:NuGetInteractive=false', '-p:RestoreIgnoreFailedSources=false']
        run(layer + '.restore', command, env, 150)
    summary['assets'] = {}
    for layer in ('Domain', 'Application', 'Infrastructure', 'Api', 'DatabaseMigrator', 'Tests'):
        relative = ('tests/' if layer == 'Tests' else 'src/') + 'RATools.' + layer + '/obj/project.assets.json'
        data = (root / relative).read_bytes()
        assets = json.loads(data)
        restore = assets['project']['restore']
        if {Path(folder).resolve() for folder in assets['packageFolders']} != {(root / 'packages').resolve()}:
            raise RuntimeError('Unexpected package folder: ' + layer)
        if set(restore['sources']) != {'https://api.nuget.org/v3/index.json'} or restore.get('fallbackFolders'):
            raise RuntimeError('Unexpected source or fallback: ' + layer)
        summary['assets'][relative] = hashlib.sha256(data).hexdigest()
    summary['onlyFreshPackageRootAndOfficialSource'] = True
    summary['exitCode'] = 0
except Exception as error:
    summary['error'] = str(error)
finally:
    summary['recycledEntries'] = len(list((root / 'recycle').iterdir())) if (root / 'recycle').exists() else 0
    (root / 'summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary), flush=True)
raise SystemExit(summary['exitCode'])
