"""Build current backend sources and run the PostgreSQL prerequisite classes."""
import hashlib
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from collections import Counter

root = Path.cwd()
config = json.loads(Path(sys.argv[1]).read_text())
platform_root = Path(config['platformRoot'])
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
results = root / ('postgres-tests-' + uuid.uuid4().hex)
results.mkdir()
summary = {'exitCode': 1, 'steps': [], 'scope': 'Actual Linux MSBuild and PostgreSQL classes; not native PDF/full suite'}
server = None
native_classes = {'RATools.Tests.DocumentProcessing.PostgresPdfContentTests',
                  'RATools.Tests.DocumentProcessing.PostgresPdfRenameTests',
                  'RATools.Tests.DocumentProcessing.PostgresProcessingAdoptionTests'}


def owned_native_processes():
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            executable = os.readlink(entry / 'exe')
            if executable.startswith(str(platform_root / 'postgres-tools') + '/'):
                fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                if fields[0] not in {'Z', 'X'}:
                    found.append({'pid': int(entry.name), 'start': fields[19], 'executable': executable})
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            pass
    return found


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


def stop(process):
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait(timeout=10)
    deadline = time.monotonic() + 5
    while group_processes(process.pid) and time.monotonic() < deadline:
        time.sleep(.05)
    return group_processes(process.pid)


def run(name, command, env, timeout=180, allow_failure=False):
    with (results / (name + '.log')).open('x') as log:
        process = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        timed_out = False
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
        finally:
            remaining = stop(process)
            summary['steps'].append({'name': name, 'exitCode': process.returncode,
                                     'timedOut': timed_out, 'remainingProcesses': remaining})
    if timed_out or remaining or (process.returncode and not allow_failure):
        raise RuntimeError(name + ' failed; inspect retained log')
    return process.returncode


try:
    if 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Inherited project no-delete guard required')
    for relative, digest in config['sources'].items():
        path = root / relative
        if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Current source mismatch: ' + relative)
    env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'PATH': str(sdk) + ':' + os.environ['PATH'],
           'DOTNET_CLI_HOME': str(root / 'cli'), 'DOTNET_EnableDiagnostics': '0',
           'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'MSBUILDDISABLENODEREUSE': '1',
           'MSBUILDPRESERVETOOLTEMPFILES': '1', 'NUGET_PACKAGES': str(root / 'packages'),
           'TMPDIR': str(root / 'temp'), 'TEMP': str(root / 'temp'), 'TMP': str(root / 'temp'),
           'LD_PRELOAD': str(root / 'recycle.so'), 'QA07_PG_ROOT': str(root),
           'QA07_PG_RECYCLE': str(root / 'recycle'), 'RATOOLS_TEST_REPOSITORY_ROOT': str(root),
           'ASPNETCORE_TEST_CONTENTROOT_RATOOLS_API': str(root / 'src/RATools.Api'), 'CI': 'true'}
    for layer in ('Api', 'DatabaseMigrator', 'Tests'):
        project = ('tests/' if layer == 'Tests' else 'src/') + 'RATools.' + layer + '/RATools.' + layer + '.csproj'
        run('build-' + layer, [str(sdk / 'dotnet'), 'build', project, '--no-restore', '-c', 'Release',
            '--nologo', '--verbosity', 'minimal', '-warnaserror', '-maxcpucount:1', '-nodeReuse:false',
            '-p:UseSharedCompilation=false'], env)
    owned = platform_root / 'postgres-task'
    tools = platform_root / 'postgres-tools'
    cluster = owned / 'cluster'
    if (cluster / 'postmaster.pid').exists() or owned_native_processes():
        raise RuntimeError('Existing cluster appears active; do not start another server')
    pg_env = {**env, 'LD_PRELOAD': str(tools / 'recycle-postgres.so'),
              'QA07_PG_ROOT': str(owned), 'QA07_PG_RECYCLE': str(owned / 'recycle'),
              'LD_LIBRARY_PATH': str(tools / 'libpq5/usr/lib/x86_64-linux-gnu'), 'LC_ALL': 'C',
              'PGCONNECT_TIMEOUT': '2'}
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    with (results / 'postgres.log').open('x') as log:
        server = subprocess.Popen([str(tools / 'postgresql-16/usr/lib/postgresql/16/bin/postgres'),
            '-D', str(cluster), '-h', '127.0.0.1', '-p', str(port), '-k', '',
            '-c', 'dynamic_shared_memory_type=mmap'], env=pg_env, stdout=log, stderr=subprocess.STDOUT,
            start_new_session=True)
    client = [str(tools / 'postgresql-client-16/usr/lib/postgresql/16/bin/psql'), '-X',
              '-h', '127.0.0.1', '-p', str(port), '-U', 'qa07', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1']
    ready = False
    for attempt in range(100):
        if server.poll() is not None:
            break
        name = 'ready-' + str(attempt)
        probe = run(name, client + ['-Atc', 'SELECT 1'], pg_env, 5, allow_failure=True)
        if probe == 0 and (results / (name + '.log')).read_text().strip() == '1':
            ready = True
            break
        time.sleep(.1)
    if not ready:
        raise RuntimeError('Database startup not confirmed')
    database = 'qa07_backend_' + uuid.uuid4().hex
    run('create-database', client + ['-c', 'CREATE DATABASE "' + database + '"'], pg_env, 30)
    connection = f'Host=127.0.0.1;Port={port};Database={database};Username=qa07;Pooling=false'
    env.update(RATOOLS_TEST_POSTGRES=connection, ConnectionStrings__PostgreSql=connection)
    migrator = str(root / 'src/RATools.DatabaseMigrator/bin/Release/net8.0/RATools.DatabaseMigrator.dll')
    run('migrate', [str(sdk / 'dotnet'), migrator], env)
    run('migrate-repeat', [str(sdk / 'dotnet'), migrator], env)
    if 'no migrations were applied' not in (results / 'migrate-repeat.log').read_text():
        raise RuntimeError('Repeated migrator did not confirm current schema')
    assembly = str(root / 'tests/RATools.Tests/bin/Release/net8.0/RATools.Tests.dll')
    run('discovery', [str(sdk / 'dotnet'), 'vstest', assembly, '/ListTests'], env)
    discovered = [line.strip() for line in (results / 'discovery.log').read_text().splitlines()
                  if line.strip().startswith('RATools.Tests.')]
    classes = [c for c in config['classes'] if c not in native_classes]
    summary['excludedNativeClasses'] = sorted(native_classes & set(config['classes']))
    selected = [name for name in discovered if any(name.startswith(c + '.') for c in classes)]
    if not selected or any(not any(name.startswith(c + '.') for name in selected) for c in classes):
        raise RuntimeError('Missing requested PostgreSQL class')
    (results / 'selected-names.json').write_text(json.dumps(selected, indent=2))
    filters = '|'.join('FullyQualifiedName~' + c + '.' for c in classes)
    run('tests', [str(sdk / 'dotnet'), 'vstest', assembly, '/TestCaseFilter:' + filters,
        '/Logger:trx;LogFileName=postgres.trx', '/ResultsDirectory:' + str(results)], env, 240)
    trx = ET.parse(results / 'postgres.trx')
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    cases = trx.findall('t:Results/t:UnitTestResult', ns)
    if Counter(c.attrib['testName'] for c in cases) != Counter(selected):
        raise RuntimeError('Executed names differ from selected discovery names')
    if any(c.attrib['outcome'] != 'Passed' for c in cases):
        raise RuntimeError('PostgreSQL cases failed or were skipped')
    summary['tests'] = {'selected': len(selected), 'passed': len(cases), 'failed': 0, 'skipped': 0}
    summary['databaseRetained'] = database
    summary['exitCode'] = 0
except Exception as error:
    summary['error'] = str(error)
finally:
    if server is not None:
        try:
            os.killpg(server.pid, signal.SIGINT)
            server.wait(timeout=20)
        except (ProcessLookupError, subprocess.TimeoutExpired):
            pass
        remaining = stop(server)
        summary['serverExitCode'] = server.returncode
        summary['remainingServerProcesses'] = remaining
        if server.returncode or remaining:
            summary['exitCode'] = 1
    survivors = owned_native_processes()
    summary['cleanupNativeProcesses'] = survivors
    # Only this invocation's server may authorize cleanup of this tool root.
    if server is not None:
        for item in survivors:
            if item in owned_native_processes():
                try:
                    os.kill(item['pid'], signal.SIGKILL)
                except ProcessLookupError:
                    pass
        deadline = time.monotonic() + 5
        while owned_native_processes() and time.monotonic() < deadline:
            time.sleep(.05)
    summary['remainingNativeProcesses'] = owned_native_processes()
    if summary['remainingNativeProcesses']:
        summary['exitCode'] = 1
    (results / 'summary.json').write_text(json.dumps(summary, indent=2))
    (root / (Path(sys.argv[1]).stem + '.result-path.txt')).write_text(str(results))
print(json.dumps({'results': str(results), **summary}), flush=True)
raise SystemExit(summary['exitCode'])
