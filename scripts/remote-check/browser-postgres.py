"""Coordinate the existing browser restart protocol with retained PostgreSQL."""
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import time
import uuid

root = Path.cwd()
results = root / 'results'
config = json.loads((root / 'browser-config.json').read_text())
platform = Path(config['postgresPlatform'])
tools, owned = platform / 'postgres-tools', platform / 'postgres-task'
record = {'exitCode': 1, 'shutdownProcesses': [], 'steps': []}
server = browser = None


def natives():
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


def stop_server():
    global server
    if server is not None:
        if server.poll() is None:
            server.send_signal(signal.SIGINT)
            server.wait(timeout=20)
        remaining = natives()
        record['shutdownProcesses'].append(remaining)
        if server.returncode or remaining:
            raise RuntimeError('Database did not stop cleanly')
        server = None


try:
    if natives() or (owned / 'cluster/postmaster.pid').exists():
        raise RuntimeError('Existing platform database process; refuse shared execution')
    with (results / 'postgres-lifecycle-owned.json').open('x') as stream:
        json.dump({'taskRoot': str(root), 'tools': str(tools), 'acquiredBeforeServerStart': True}, stream)
    pg_env = {**os.environ, 'LD_PRELOAD': str(tools / 'recycle-postgres.so'),
              'QA07_PG_ROOT': str(owned), 'QA07_PG_RECYCLE': str(owned / 'recycle'),
              'LD_LIBRARY_PATH': str(tools / 'libpq5/usr/lib/x86_64-linux-gnu'),
              'PGCONNECT_TIMEOUT': '2', 'LC_ALL': 'C'}
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    client = [str(tools / 'postgresql-client-16/usr/lib/postgresql/16/bin/psql'), '-X',
              '-h', '127.0.0.1', '-p', str(port), '-U', 'qa07', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1']

    def start_server(label):
        global server
        with (results / (label + '.log')).open('x') as log:
            server = subprocess.Popen([str(tools / 'postgresql-16/usr/lib/postgresql/16/bin/postgres'),
                '-D', str(owned / 'cluster'), '-h', '127.0.0.1', '-p', str(port), '-k', '',
                '-c', 'dynamic_shared_memory_type=mmap'], env=pg_env, stdout=log, stderr=subprocess.STDOUT)
        ready_deadline = time.monotonic() + 20
        while time.monotonic() < ready_deadline:
            if server.poll() is not None:
                break
            probe = subprocess.run(client + ['-Atc', 'SELECT 1'], env=pg_env, capture_output=True, timeout=5)
            if probe.returncode == 0 and probe.stdout.strip() == b'1':
                return
            time.sleep(.1)
        raise RuntimeError('PostgreSQL did not become ready')

    start_server('pg-browser-first')
    database = 'qa07_browser_' + uuid.uuid4().hex
    with (results / 'pg-browser-create.log').open('x') as log:
        created = subprocess.run(client + ['-c', 'CREATE DATABASE "' + database + '"'],
                                 env=pg_env, stdout=log, stderr=subprocess.STDOUT, timeout=20)
    if created.returncode:
        raise RuntimeError('Database creation failed')
    product_env = {**os.environ, 'QA07_POSTGRES': 'true',
                   'ConnectionStrings__PostgreSql': f'Host=127.0.0.1;Port={port};Username=qa07;Database={database};Pooling=false'}
    with (results / 'pg-browser.log').open('x') as log:
        browser = subprocess.Popen(['node', '--import', '../scripts/remote-check/no-delete.mjs',
            '../scripts/remote-check/browser-backend.mjs'], cwd=root / 'frontend', env=product_env,
            stdout=log, stderr=subprocess.STDOUT)
        deadline, restarted = time.monotonic() + 240, False
        while browser.poll() is None:
            if (results / 'postgres-restart-request').exists() and not restarted:
                stop_server()
                start_server('pg-browser-restarted')
                (results / 'postgres-restart-ready').write_text('database restarted')
                restarted = True
            if time.monotonic() > deadline:
                raise TimeoutError('PostgreSQL browser deadline exceeded')
            time.sleep(.1)
        record['browserExitCode'] = browser.returncode
        if browser.returncode or not restarted:
            raise RuntimeError('Browser did not complete database restart')
    stop_server()
    record.update(exitCode=0, restarted=True, retainedDatabase=database)
except Exception as error:
    record['error'] = str(error)
finally:
    try:
        if browser is not None and browser.poll() is None:
            browser.kill()
            browser.wait(timeout=10)
    except Exception as error:
        record.update(exitCode=1, browserCleanupError=str(error))
    finally:
        try:
            stop_server()
        except Exception as error:
            record.update(exitCode=1, shutdownError=str(error))
    survivors = natives()
    record['cleanupNativeProcesses'] = survivors
    # A server reference proves this invocation entered the owned lifecycle.
    if server is not None:
        for item in survivors:
            if item in natives():
                try:
                    os.kill(item['pid'], signal.SIGKILL)
                except ProcessLookupError:
                    pass
        deadline = time.monotonic() + 5
        while natives() and time.monotonic() < deadline:
            time.sleep(.05)
    record['remainingNativeProcesses'] = natives()
    if record['remainingNativeProcesses']:
        record['exitCode'] = 1
    (results / 'postgres-browser.json').write_text(json.dumps(record, indent=2))
print(json.dumps(record), flush=True)
raise SystemExit(record['exitCode'])
