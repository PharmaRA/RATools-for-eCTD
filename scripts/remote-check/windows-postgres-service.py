"""Bounded loopback PG service for the Windows content tests; retain all data."""
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

root = Path.cwd()
config = json.loads((root / 'service-config.json').read_text())
platform = Path(config['platformRoot'])
tools = platform / 'postgres-tools'
owned = platform / 'postgres-task'
cluster = owned / 'cluster'
results = root / 'results'
results.mkdir()
summary = {'exitCode': 1, 'steps': [], 'scope': 'Retained remote loopback PG for Windows native acceptance'}
server = None
stopping = False
stop_reason = None


def request_stop(signum, frame):
    global stopping, stop_reason
    stopping = True
    stop_reason = 'signal'


signal.signal(signal.SIGTERM, request_stop)
signal.signal(signal.SIGINT, request_stop)


def native_processes():
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            executable = os.readlink(entry / 'exe')
            if executable.startswith(str(tools) + '/'):
                fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                if fields[0] not in {'Z', 'X'}:
                    found.append({'pid': int(entry.name), 'start': fields[19]})
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass
    return found


def group_members(group):
    members = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(fields[2]) == group and fields[0] not in {'Z', 'X'}:
                members.append(int(entry.name))
        except (FileNotFoundError, ProcessLookupError):
            pass
    return members


def command(name, arguments, env, timeout=15, optional=False):
    with (results / (name + '.log')).open('x') as log:
        process = subprocess.Popen(arguments, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
            raise
        finally:
            remaining = group_members(process.pid)
            if remaining:
                os.killpg(process.pid, signal.SIGKILL)
                deadline = time.monotonic() + 5
                while group_members(process.pid) and time.monotonic() < deadline:
                    time.sleep(.05)
            remaining = group_members(process.pid)
            summary['steps'].append({'name': name, 'exitCode': process.returncode, 'remainingProcesses': remaining})
            if remaining:
                raise RuntimeError('Client process group survived')
    if process.returncode and not optional:
        raise RuntimeError('Database command failed: ' + name)
    return process.returncode


try:
    if 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Project no-delete launch required')
    for relative, expected in config['platformHashes'].items():
        path = platform / relative
        if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise RuntimeError('Qualified platform input differs')
    if (cluster / 'postmaster.pid').exists() or native_processes():
        raise RuntimeError('Existing PostgreSQL ownership refused')
    with (root / 'ownership.json').open('x') as marker:
        json.dump({'runId': config['runId'], 'platformRoot': str(platform)}, marker)
    env = {key: value for key, value in os.environ.items() if not key.startswith('PG')}
    env.update(LD_PRELOAD=str(tools / 'recycle-postgres.so'), QA07_PG_ROOT=str(owned),
               QA07_PG_RECYCLE=str(owned / 'recycle'),
               LD_LIBRARY_PATH=str(tools / 'libpq5/usr/lib/x86_64-linux-gnu'),
               LC_ALL='C', PGCONNECT_TIMEOUT='2', PGPASSFILE=str(root / 'absent-pgpass'))
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    with (results / 'postgres.log').open('x') as log:
        server = subprocess.Popen([str(tools / 'postgresql-16/usr/lib/postgresql/16/bin/postgres'),
            '-D', str(cluster), '-h', '127.0.0.1', '-p', str(port), '-k', '',
            '-c', 'dynamic_shared_memory_type=mmap'], env=env, stdout=log, stderr=subprocess.STDOUT,
            start_new_session=True)
    client = [str(tools / 'postgresql-client-16/usr/lib/postgresql/16/bin/psql'), '-X', '-h', '127.0.0.1',
              '-p', str(port), '-U', 'qa07', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1']
    for attempt in range(50):
        if server.poll() is not None:
            raise RuntimeError('Server exited during startup')
        name = 'ready-' + str(attempt)
        if command(name, client + ['-Atc', 'SELECT 1'], env, optional=True) == 0:
            if (results / (name + '.log')).read_text().strip() == '1':
                break
        time.sleep(.1)
    else:
        raise RuntimeError('Server readiness deadline')
    database = 'qa07_windows_' + uuid.uuid4().hex
    command('create-database', client + ['-c', 'CREATE DATABASE "' + database + '"'], env, 30)
    identity_sql = "SELECT json_build_object('port',inet_server_port(),'version',current_setting('server_version'),'started',pg_postmaster_start_time()::text)"
    command('server-identity', client + ['-Atc', identity_sql], env)
    identity = json.loads((results / 'server-identity.log').read_text())
    (results / 'ready.json').write_text(json.dumps({'port': port, 'database': database, 'pid': server.pid,
        'runId': config['runId'], 'serverIdentity': identity,
        'driverSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'guardSha256': hashlib.sha256((root / 'no-delete.py').read_bytes()).hexdigest()}))
    deadline = time.monotonic() + 400
    while not stopping:
        stop_path = root / 'stop-requested'
        if stop_path.exists():
            if stop_path.is_symlink() or json.loads(stop_path.read_text()) != {'runId': config['runId']}:
                raise RuntimeError('Stop marker ownership mismatch')
            stop_reason = 'owned-marker'
            break
        if server.poll() is not None:
            raise RuntimeError('Server unexpectedly exited')
        if time.monotonic() >= deadline:
            stop_reason = 'runtime-deadline'
            raise TimeoutError('Bounded database service deadline')
        time.sleep(.2)
    if stop_reason != 'owned-marker':
        raise RuntimeError('Service did not receive an owned stop marker')
    summary['exitCode'] = 0
except Exception as error:
    summary['error'] = type(error).__name__ + ': ' + str(error)
finally:
    summary['stopReason'] = stop_reason
    if server is not None:
        if server.poll() is None:
            server.send_signal(signal.SIGINT)
            try:
                server.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(server.pid, signal.SIGKILL)
                server.wait(timeout=10)
                summary['exitCode'] = 1
                summary['forcedServerStop'] = True
        summary['serverExitCode'] = server.returncode
        summary['serverGroupRemaining'] = group_members(server.pid)
        if server.returncode or summary['serverGroupRemaining']:
            summary['exitCode'] = 1
    summary['nativeProcessesRemaining'] = native_processes()
    if summary['nativeProcessesRemaining']:
        summary['exitCode'] = 1
    (results / 'summary.json').write_text(json.dumps(summary, indent=2))
sys.exit(summary['exitCode'])
