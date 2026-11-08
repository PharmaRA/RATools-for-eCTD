"""Current MSBuild API and freshly built static frontend, with retained download."""
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import time

root = Path.cwd()
config = json.loads((root / 'browser-config.json').read_text())
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
results = root / 'results'
results.mkdir()
summary = {'exitCode': 1, 'steps': [], 'scope': 'Current MSBuild API/InMemory and fresh frontend, real browser download'}


def group_processes(group):
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


def profile_processes():
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            arguments = (entry / 'cmdline').read_bytes().split(b'\0')
            if any(arg.startswith(('--user-data-dir=' + str(root) + '/browser-profile-').encode()) for arg in arguments):
                fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                if fields[0] not in {'Z', 'X'}:
                    found.append({'pid': int(entry.name), 'start': fields[19]})
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass
    return found


def postgres_processes():
    tools = Path(config['postgresPlatform']) / 'postgres-tools'
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            executable = os.readlink(entry / 'exe')
            if executable.startswith(str(tools) + '/'):
                fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                if fields[0] not in {'Z', 'X'}:
                    found.append({'pid': int(entry.name), 'start': fields[19], 'executable': executable})
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass
    return found


def run(name, command, env, cwd, timeout):
    with (results / (name + '.log')).open('x') as log:
        process = subprocess.Popen(command, env=env, cwd=cwd, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
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
            while group_processes(process.pid) and time.monotonic() < deadline:
                time.sleep(.05)
            remaining = group_processes(process.pid)
            summary['steps'].append({'name': name, 'exitCode': process.returncode,
                                     'timedOut': timed_out, 'remainingProcesses': remaining})
    if process.returncode or timed_out or remaining:
        raise RuntimeError(name + ' failed; see retained log')


try:
    if 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Project no-delete launch required')
    for relative, digest in config['inputs'].items():
        path = root / relative
        if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Uploaded input differs: ' + relative)
    source = Path(config['apiOutput'])
    for path in (source, *source.parents):
        if path.is_symlink():
            raise RuntimeError('API output root has a link')
    backend = root / 'backend'
    backend.mkdir()
    copied = {}
    for path in sorted(source.rglob('*')):
        if path.is_symlink():
            raise RuntimeError('Linked API output')
        if path.is_file():
            relative = path.relative_to(source)
            target = backend / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            data = path.read_bytes()
            with target.open('xb') as stream:
                stream.write(data)
            copied[str(relative)] = hashlib.sha256(data).hexdigest()
    if copied != config['expectedApiOutputs']:
        raise RuntimeError('API output differs from the fixed source output manifest')
    (results / 'api-output-manifest.json').write_text(json.dumps(copied, indent=2))
    if config.get('postgresPlatform'):
        summary['scope'] = 'Current MSBuild API/PostgreSQL and fresh frontend with real download and API/database restart'
        migrator = Path(config['migratorOutput'])
        for name, digest in config['expectedMigratorOutputs'].items():
            path = migrator / name
            for ancestor in (path, *path.parents):
                if ancestor.is_symlink():
                    raise RuntimeError('Linked migrator output')
            data = path.read_bytes()
            if hashlib.sha256(data).hexdigest() != digest:
                raise RuntimeError('Migrator output mismatch')
            destination = backend / 'migrator' / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open('xb') as stream:
                stream.write(data)
    env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_EnableDiagnostics': '0',
           'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
           'QA07_POSTGRES': 'false'}
    if config.get('postgresPlatform'):
        env['QA07_MIGRATOR_DLL'] = str(backend / 'migrator/RATools.DatabaseMigrator.dll')
    refs = []
    for pack in ('Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref'):
        refs += sorted((sdk / 'packs' / pack / '8.0.29/ref/net8.0').glob('*.dll'))
    run('startup-compile', [str(sdk / 'dotnet'), str(sdk / 'sdk/8.0.423/Roslyn/bincore/csc.dll'),
        '-nologo', '-target:library', '-warnaserror+', '-out:' + str(backend / 'BrowserStartup.dll'),
        *['-r:' + str(path) for path in refs], str(root / 'scripts/remote-check/BrowserStartup.cs')], env, root, 30)
    # Preserve the real MSBuild dependency manifest, then add only the task startup library.
    deps = backend / 'RATools.Api.deps.json'
    original = deps.read_bytes()
    retained = backend / 'RATools.Api.deps.original.json'
    deps.rename(retained)
    document = json.loads(original)
    target = document['runtimeTarget']['name']
    document['targets'][target]['BrowserStartup/1.0.0'] = {'runtime': {'BrowserStartup.dll': {}}}
    document['libraries']['BrowserStartup/1.0.0'] = {'type': 'project', 'serviceable': False, 'sha512': ''}
    with deps.open('x') as stream:
        json.dump(document, stream)
    if config.get('postgresPlatform'):
        run('postgres-browser', ['python3', '-B', 'scripts/remote-check/browser-postgres.py'], env, root, 290)
    else:
        run('browser', ['node', '--import', '../scripts/remote-check/no-delete.mjs', '../scripts/remote-check/browser-backend.mjs'],
            env, root / 'frontend', 180)
    report = json.loads((results / 'backend-browser.json').read_text())
    if report['errors'] or not any(event.get('state') == 'completed' for event in report['downloadEvents']):
        raise RuntimeError('Browser download completion not established')
    download = Path(report['facts']['download']['retainedPath'])
    download.resolve().relative_to(root.resolve())
    for path in (download, *download.parents):
        if path.is_symlink():
            raise RuntimeError('Linked browser download path')
    run('download-verification', ['python3', '-B', 'scripts/remote-check/verify-pdf-download.py'], env, root, 30)
    with (results / 'delivery-package.zip').open('xb') as stream:
        stream.write(download.read_bytes())
    summary['browserChecks'] = len(report['passed'])
    summary['download'] = report['facts']['download']
    summary['exitCode'] = 0
except Exception as error:
    summary['error'] = str(error)
finally:
    marker = results / 'postgres-lifecycle-owned.json'
    if config.get('postgresPlatform') and marker.exists():
        ownership = json.loads(marker.read_text())
        expected = {'taskRoot': str(root), 'tools': str(Path(config['postgresPlatform']) / 'postgres-tools'),
                    'acquiredBeforeServerStart': True}
        if marker.is_symlink() or ownership != expected:
            summary.update(exitCode=1, postgresCleanupError='Invalid lifecycle ownership marker')
        else:
            remaining = postgres_processes()
            summary['postgresProcessesBeforeOuterCleanup'] = remaining
            for item in remaining:
                if item in postgres_processes():
                    try:
                        os.kill(item['pid'], signal.SIGKILL)
                    except ProcessLookupError:
                        pass
            deadline = time.monotonic() + 5
            while postgres_processes() and time.monotonic() < deadline:
                time.sleep(.05)
            summary['remainingPostgresProcesses'] = postgres_processes()
            if summary['remainingPostgresProcesses']:
                summary['exitCode'] = 1
    survivors = profile_processes()
    summary['profileProcessesBeforeCleanup'] = survivors
    for item in survivors:
        if item in profile_processes():
            try:
                os.kill(item['pid'], signal.SIGKILL)
            except ProcessLookupError:
                pass
    deadline = time.monotonic() + 5
    while profile_processes() and time.monotonic() < deadline:
        time.sleep(.05)
    summary['remainingProfileProcesses'] = profile_processes()
    if summary['remainingProfileProcesses']:
        summary['exitCode'] = 1
    (results / 'summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary), flush=True)
raise SystemExit(summary['exitCode'])
