"""Actual qpdf cases under inherited no-delete guard and delegated cgroup v2."""
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import signal
import stat
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

root = Path.cwd()
config = json.loads((root / 'qpdf-config.json').read_text())
repo = Path(config['repository'])
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
results = root / 'results'
group = Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service') / ('ratools-' + root.name)
unit = 'ratools-' + root.name + '.service'


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


def cleanup_postgres():
    marker = results / 'postgres-owned.json'
    if not config.get('postgresPlatform') or not marker.exists():
        return []
    if marker.is_symlink() or json.loads(marker.read_text()) != {'root': str(root), 'platform': config['postgresPlatform']}:
        raise RuntimeError('Invalid PostgreSQL lifecycle marker')
    for item in postgres_processes():
        if item in postgres_processes():
            try:
                os.kill(item['pid'], signal.SIGKILL)
            except ProcessLookupError:
                pass
    deadline = time.monotonic() + 5
    while postgres_processes() and time.monotonic() < deadline:
        time.sleep(.05)
    return postgres_processes()


def live_group(identity):
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(fields[2]) == identity and fields[0] not in {'Z', 'X'}:
                found.append(int(entry.name))
        except (FileNotFoundError, ProcessLookupError):
            pass
    return found


def stop_unit():
    subprocess.run(['systemctl', '--user', 'kill', '--kill-whom=all', '--signal=SIGKILL', unit],
                   capture_output=True, timeout=10, check=False)
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        value = subprocess.check_output(['systemctl', '--user', 'show', unit, '--property=MainPID,ControlGroup'], text=True, timeout=10)
        state = dict(line.split('=', 1) for line in value.splitlines() if '=' in line)
        relative = state.get('ControlGroup', '')
        events = 'absent'
        if relative:
            control = Path('/sys/fs/cgroup') / relative.lstrip('/')
            if control.name != unit or not str(control).startswith('/sys/fs/cgroup/user.slice/'):
                raise RuntimeError('Unexpected owned unit cgroup')
            if control.exists():
                events = (control / 'cgroup.events').read_text()
        if state.get('MainPID') == '0' and 'populated 1' not in events:
            return {'mainPid': 0, 'controlGroup': relative, 'events': events}
        time.sleep(.1)
    raise RuntimeError('Task unit still contains live processes')


def worker():
    report = {'exitCode': 1, 'scope': 'Eight existing qpdf engine/workflow/HTTP classes; not Python/full native suite', 'steps': []}
    try:
        group.mkdir()
        (group / 'cgroup.subtree_control').write_text('+cpu +memory +pids')
        env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_EnableDiagnostics': '0',
               'DOTNET_CLI_HOME': str(repo / 'cli'), 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
               'RATOOLS_TEST_REPOSITORY_ROOT': str(repo), 'RATOOLS_PDF_CGROUP_ROOT': str(group),
               'RATOOLS_QPDF': str(root / 'qpdf/bin/qpdf'), 'LD_LIBRARY_PATH': str(root / 'qpdf/lib'),
               'RATOOLS_TEST_PYTHON': '/usr/bin/python3', 'MSBUILDPRESERVETOOLTEMPFILES': '1',
               'MSBUILDDISABLENODEREUSE': '1',
               'LD_PRELOAD': str(repo / 'recycle.so'), 'QA07_PG_ROOT': str(repo),
               'QA07_PG_RECYCLE': str(repo / 'recycle'), 'TMPDIR': str(repo / 'temp'),
               'TEMP': str(repo / 'temp'), 'TMP': str(repo / 'temp')}
        env.pop('RATOOLS_TEST_POSTGRES', None)
        version = subprocess.check_output([env['RATOOLS_QPDF'], '--version'], env=env, text=True, timeout=10)
        if version.splitlines()[0] != 'qpdf version 12.4.1':
            raise RuntimeError('Wrong qpdf version')
        report['version'] = version.splitlines()[0]
        assembly = repo / 'tests/RATools.Tests/bin/Release/net8.0/RATools.Tests.dll'
        if hashlib.sha256(assembly.read_bytes()).hexdigest() != config['testAssemblySha256']:
            raise RuntimeError('Verified test assembly changed')

        def run(name, command):
            with (results / (name + '.log')).open('x') as log:
                p = subprocess.Popen(command, cwd=repo, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                try:
                    p.wait(timeout=180)
                finally:
                    try:
                        os.killpg(p.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    p.wait(timeout=10)
                    deadline = time.monotonic() + 5
                    while live_group(p.pid) and time.monotonic() < deadline:
                        time.sleep(.05)
                    remaining = live_group(p.pid)
            report['steps'].append({'name': name, 'exitCode': p.returncode, 'remainingProcesses': remaining})
            if remaining:
                raise RuntimeError('VSTest process group survived')
            return p.returncode

        if config.get('rebuildTests'):
            if run('build-tests', [str(sdk / 'dotnet'), 'build', 'tests/RATools.Tests/RATools.Tests.csproj',
                '--no-restore', '-c', 'Release', '--nologo', '--verbosity', 'minimal', '-warnaserror',
                '-maxcpucount:1', '-nodeReuse:false', '-p:UseSharedCompilation=false']):
                raise RuntimeError('Current test source compilation failed')
            report['rebuiltTestAssemblySha256'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
        if run('discovery', [str(sdk / 'dotnet'), 'vstest', str(assembly), '/ListTests']):
            raise RuntimeError('Discovery failed')
        classes = ['RATools.Tests.DocumentProcessing.' + name for name in (
            'QpdfProcessingEngineTests', 'QpdfNavigationIntegrityTests', 'PdfRenameEngineTests',
            'PdfRenamePreviewTests', 'PdfRenameWorkflowTests', 'PdfRenameHttpTests',
            'ProcessingWorkflowTests', 'ProcessingHttpTests')]
        discovered = [line.strip() for line in (results / 'discovery.log').read_text().splitlines()]
        names = [line for line in discovered
                 if any(line.strip().startswith(c + '.') for c in classes)]
        portable = ['RATools.Tests.DocumentProcessing.QpdfConcurrencyTests.' + name for name in (
            'StderrFailureDrainsThePendingOutputWriteBeforeDisposingStreamsOrReleasingTheSlot',
            'InspectAndExecuteShareTheInjectedBudgetAcrossEngineInstances',
            'StartFailureAndCallbackFailureReleaseTheOnlyProcessSlot',
            'DefaultProcessBudgetQueuesTheThirdInspectionAndCancellationNeverStartsIt')]
        if config.get('portableConcurrency'):
            if any(name not in discovered for name in portable):
                raise RuntimeError('Portable concurrency method absent')
            names += portable
        counts = {c: sum(name.startswith(c + '.') for name in names) for c in classes}
        if not all(counts.values()):
            raise RuntimeError('A requested qpdf test class is absent')
        report['selectedClasses'] = counts
        report['portableConcurrencyMethods'] = portable if config.get('portableConcurrency') else []
        (results / 'selected-names.json').write_text(json.dumps(names, indent=2))
        filters = ['FullyQualifiedName~' + c + '.' for c in classes]
        if config.get('portableConcurrency'):
            filters += ['FullyQualifiedName=' + name for name in portable]
        code = run('tests', [str(sdk / 'dotnet'), 'vstest', str(assembly),
            '/TestCaseFilter:' + '|'.join(filters),
            '/Logger:trx;LogFileName=qpdf.trx', '/ResultsDirectory:' + str(results)])
        document = ET.parse(results / 'qpdf.trx')
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        cases = document.findall('t:Results/t:UnitTestResult', ns)
        report['tests'] = {'total': len(cases), 'passed': sum(c.attrib['outcome'] == 'Passed' for c in cases)}
        if Counter(c.attrib['testName'] for c in cases) != Counter(names):
            raise RuntimeError('Discovery and execution name multiset differ')
        if code or any(c.attrib['outcome'] != 'Passed' for c in cases):
            raise RuntimeError('qpdf tests failed or skipped')
        if config.get('postgresPlatform'):
            platform = Path(config['postgresPlatform'])
            if postgres_processes() or (platform / 'postgres-task/cluster/postmaster.pid').exists():
                raise RuntimeError('Existing PostgreSQL lifecycle; refuse ownership')
            with (results / 'postgres-owned.json').open('x') as stream:
                json.dump({'root': str(root), 'platform': str(platform)}, stream)
            pg_config = root / ('pg-qpdf-' + root.name + '.json')
            with pg_config.open('x') as stream:
                json.dump({'platformRoot': str(platform), 'sources': config['sourceHashes'], 'qpdfOnly': True,
                    'classes': ['RATools.Tests.DocumentProcessing.PostgresPdfRenameTests',
                                'RATools.Tests.DocumentProcessing.PostgresProcessingAdoptionTests']}, stream)
            code = run('postgres-qpdf', ['python3', '-B', str(root / 'postgres-tests.py'), str(pg_config)])
            pointer = repo / (pg_config.stem + '.result-path.txt')
            pg_results = Path(pointer.read_text())
            pg_results.resolve().relative_to(repo.resolve())
            pg_summary = json.loads((pg_results / 'summary.json').read_text())
            report['postgresResults'] = str(pg_results)
            report['postgresTests'] = pg_summary.get('tests')
            if code or pg_summary['exitCode'] or pg_summary.get('tests', {}).get('passed') != 7:
                raise RuntimeError('PostgreSQL/qpdf seven-case acceptance failed')
        report['exitCode'] = 0
    except Exception as error:
        report['error'] = str(error)
    finally:
        report['remainingPostgresProcesses'] = cleanup_postgres()
        if report['remainingPostgresProcesses']:
            report['exitCode'] = 1
        if group.exists():
            (group / 'cgroup.kill').write_text('1')
            deadline = time.monotonic() + 5
            while 'populated 1' in (group / 'cgroup.events').read_text() and time.monotonic() < deadline:
                time.sleep(.05)
            report['resourceGroups'] = {p.name: (p / 'cgroup.events').read_text() for p in group.iterdir() if p.is_dir()}
            report['resourceRootEvents'] = (group / 'cgroup.events').read_text()
            if 'populated 1' in report['resourceRootEvents']:
                report['exitCode'] = 1
        (results / 'summary.json').write_text(json.dumps(report, indent=2))
    return report['exitCode']


if '--worker' in sys.argv:
    raise SystemExit(worker())

results.mkdir()
for relative, digest in config['sourceHashes'].items():
    path = repo / relative
    if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise RuntimeError('Verified backend source changed: ' + relative)
archive = root / 'qpdf.zip'
if hashlib.sha256(archive.read_bytes()).hexdigest() != config['archiveSha256']:
    raise RuntimeError('Official qpdf archive digest differs')
with zipfile.ZipFile(archive) as package:
    for entry in package.infolist():
        relative = Path(entry.filename)
        if relative.is_absolute() or '..' in relative.parts:
            raise RuntimeError('Unsafe package path')
        data = package.read(entry)
        if stat.S_ISLNK(entry.external_attr >> 16):
            if entry.filename != 'lib/libqpdf.so.30' or data != b'libqpdf.so.30.4.1':
                raise RuntimeError('Unreviewed package link')
            data = package.read('lib/libqpdf.so.30.4.1')
        elif not stat.S_ISREG(entry.external_attr >> 16):
            raise RuntimeError('Special package entry')
        target = root / 'qpdf' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write(data)
        target.chmod(0o755 if entry.filename.startswith('bin/') else 0o644)
fixture = repo / 'tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf'
data = (root / 'navigation-source.pdf').read_bytes()
if hashlib.sha256(data).hexdigest() != config['fixtureSha256']:
    raise RuntimeError('Fixture mismatch')
for ancestor in (fixture, *fixture.parents):
    if ancestor.is_symlink():
        raise RuntimeError('Fixture path link')
if fixture.exists():
    if fixture.read_bytes() != data:
        raise RuntimeError('Existing fixture differs')
else:
    fixture.parent.mkdir(parents=True, exist_ok=True)
    with fixture.open('xb') as stream:
        stream.write(data)
launch = ['systemd-run', '--user', '--quiet', '--service-type=exec', '--unit=' + unit,
          '--property=RemainAfterExit=yes', '--working-directory=' + str(root),
          'python3', '-B', 'no-delete.py', 'python3', '-B', 'qpdf-tests.py', '--worker']
existing = subprocess.run(['systemctl', '--user', 'show', unit, '--property=LoadState'], capture_output=True, text=True, timeout=10)
if existing.stdout.strip() != 'LoadState=not-found' or group.exists():
    raise RuntimeError('Task unit/resource name already exists; refuse launch and cleanup')
try:
    subprocess.run(launch, check=True, timeout=20)
    deadline = time.monotonic() + 420
    while time.monotonic() < deadline:
        value = subprocess.check_output(['systemctl', '--user', 'show', unit,
            '--property=MainPID,ExecMainCode,ExecMainStatus,ActiveState,SubState'], text=True, timeout=10)
        state = dict(line.split('=', 1) for line in value.splitlines() if '=' in line)
        if state.get('MainPID') == '0' and state.get('ExecMainCode') not in {None, '0'}:
            exit_code = int(state['ExecMainStatus']) if state['ExecMainCode'] == '1' else 1
            unit_cleanup = stop_unit()
            if group.exists() and 'populated 1' in (group / 'cgroup.events').read_text():
                raise RuntimeError('Resource descendants survived')
            (results / 'manager.json').write_text(json.dumps({'command': launch, 'state': state, 'exitCode': exit_code,
                'unitCleanup': unit_cleanup}, indent=2))
            raise SystemExit(exit_code)
        time.sleep(.5)
    raise TimeoutError('qpdf worker deadline')
except Exception:
    try:
        stop_unit()
    finally:
        if group.exists():
            (group / 'cgroup.kill').write_text('1')
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            value = subprocess.check_output(['systemctl', '--user', 'show', unit, '--property=MainPID'], text=True, timeout=10)
            if 'MainPID=0' in value and (not group.exists() or 'populated 1' not in (group / 'cgroup.events').read_text()):
                break
            time.sleep(.1)
        else:
            raise RuntimeError('qpdf manager failure cleanup did not establish stopped processes')
    raise
finally:
    remaining = cleanup_postgres()
    (results / 'outer-postgres-cleanup.json').write_text(json.dumps({'remainingProcesses': remaining}, indent=2))
    if remaining:
        raise RuntimeError('PostgreSQL processes survived outer cleanup')
