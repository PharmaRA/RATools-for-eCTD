"""Retained PostgreSQL 16 tool installation and no-delete startup qualification."""
import hashlib
import json
import os
import platform
from pathlib import Path
import socket
import signal
import shutil
import subprocess
import sys
import tarfile
import time
from urllib.request import urlopen

root = Path.cwd()
results = root / 'results'
owned = root / 'postgres-task'
owned.mkdir()
recycle = owned / 'recycle'
recycle.mkdir()
tools = root / 'postgres-tools'
tools.mkdir()
phase = 'toolchain-unavailable'
evidence = {'scope': 'PostgreSQL16 isolated tools and retained cluster restart, not product API qualification', 'steps': []}
server = None


def owned_native_processes():
    found = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit(): continue
        try:
            executable = os.readlink(entry / 'exe')
            if executable.startswith(str(tools) + '/'):
                stat = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                if stat[0] not in {'Z', 'X'}:
                    found.append({'pid': int(entry.name), 'start': stat[19], 'executable': executable})
        except (FileNotFoundError, ProcessLookupError, PermissionError): pass
    return found


def live_group(group):
    # Read only stat for processes belonging to this task-owned session/group.
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit(): continue
        try:
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(fields[2]) == group and fields[0] not in {'Z', 'X'}: return True
        except (FileNotFoundError, ProcessLookupError): pass
    return False


def end_group(process):
    try: os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError: pass
    process.wait(timeout=10)
    deadline = time.monotonic() + 5
    while live_group(process.pid) and time.monotonic() < deadline: time.sleep(.05)
    if live_group(process.pid): raise RuntimeError('Owned process group did not stop')


def run(name, args, env=None):
    with (results / (name + '.log')).open('w') as log:
        value = subprocess.Popen(args, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try: value.wait(timeout=120)
        finally: end_group(value)
    evidence['steps'].append({'name': name, 'exitCode': value.returncode})
    if value.returncode:
        raise RuntimeError(name + ' failed; see its retained log')


try:
    if sys.platform != 'linux' or platform.machine() != 'x86_64' or 'Seccomp:\t2' not in Path('/proc/self/status').read_text():
        raise RuntimeError('Inherited Linux x86_64 no-delete seccomp is required')
    if not shutil.which('cc'): raise RuntimeError('C compiler is unavailable')
    shim = tools / 'recycle-postgres.so'
    phase = 'build-failed'
    run('pg-recycle-compile', ['cc', '-shared', '-fPIC', '-std=c11', '-Wall', '-Wextra', '-Werror',
        str(root / 'scripts/remote-check/RecyclePostgres.c'), '-o', str(shim)])
    env = {**os.environ, 'LD_PRELOAD': str(shim), 'QA07_PG_ROOT': str(owned), 'QA07_PG_RECYCLE': str(recycle), 'LC_ALL': 'C'}
    phase = 'test-failed'
    # These are actual libc deletion requests; the shim must preserve every byte.
    smoke = r'''
import ctypes,errno,os,pathlib
r=pathlib.Path(os.environ['QA07_PG_ROOT']); q=pathlib.Path(os.environ['QA07_PG_RECYCLE'])
f=r/'proof'; f.write_bytes(b'preserved')
os.unlink(f); assert not f.exists(); assert any(p.is_file() and p.read_bytes()==b'preserved' for p in q.iterdir())
d=r/'empty'; d.mkdir(); os.rmdir(d); assert not d.exists()
d=r/'nonempty'; d.mkdir(); (d/'child').write_bytes(b'child')
try: os.rmdir(d); raise AssertionError('nonempty directory accepted')
except OSError as e: assert e.errno==errno.ENOTEMPTY
original=r/'original'; original.write_bytes(b'original'); link=r/'link'; link.symlink_to(original)
try: os.unlink(link); raise AssertionError('symbolic link accepted')
except OSError as e: assert e.errno==errno.EACCES
hard=r/'hard'; os.link(original,hard)
try: os.unlink(hard); raise AssertionError('hard link accepted')
except OSError as e: assert e.errno==errno.EACCES
relative=r/'relative'; relative.write_bytes(b'dirfd')
fd=os.open(r,os.O_RDONLY|os.O_DIRECTORY)
try: os.unlink('relative',dir_fd=fd)
finally: os.close(fd)
assert not relative.exists() and any(p.is_file() and p.read_bytes()==b'dirfd' for p in q.iterdir())
outside=r.parent/'outside-proof'; outside.write_bytes(b'outside')
try: os.unlink(outside); raise AssertionError('outside root accepted')
except OSError as e: assert e.errno==errno.EACCES
libc=ctypes.CDLL(None,use_errno=True); f=r/'syscall-proof'; f.write_bytes(b'syscall')
assert libc.syscall(87,os.fsencode(f)) == -1 and f.read_bytes()==b'syscall'
print('PASS preserved file/dirfd/empty-directory, refused nonempty/symlink/hardlink/outside, raw deletion blocked')
'''
    run('pg-recycle-smoke', [sys.executable, '-B', '-c', smoke], env)
    phase = 'dependency-install-failed'
    lock = json.loads((root / 'scripts/remote-check/postgres-packages.json').read_text())
    if {item['name'] for item in lock['packages']} != {'postgresql-16', 'postgresql-client-16', 'libpq5'}:
        raise RuntimeError('Unexpected PostgreSQL lock package selection')
    evidence['packages'] = []
    for record in lock['packages']:
        name, address = record['name'], record['url']
        if not address.startswith('https://apt.postgresql.org/pub/repos/apt/pool/'):
            raise RuntimeError('Unexpected PostgreSQL package source')
        data = urlopen(address, timeout=60).read()
        if hashlib.sha256(data).hexdigest() != record['sha256']: raise RuntimeError('Package checksum mismatch')
        archive = tools / (name + '.deb'); archive.write_bytes(data)
        target = tools / name; target.mkdir()
        # dpkg-deb streams archive bytes only; no installation or maintainer scripts.
        run('pg-extract-' + name, ['dpkg-deb', '--fsys-tarfile', str(archive)])
        with tarfile.open(results / ('pg-extract-' + name + '.log')) as package:
            for member in package:
                relative = Path(member.name)
                if relative.is_absolute() or '..' in relative.parts: raise RuntimeError('Unsafe package path')
                destination = target / relative
                if member.isdir(): destination.mkdir(parents=True, exist_ok=True)
                elif member.isfile():
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    with destination.open('xb') as stream: stream.write(package.extractfile(member).read())
                    destination.chmod(member.mode & 0o755)
                elif not (member.issym() or member.islnk()): raise RuntimeError('Special package entry refused')
                # Package links are not created or traversed; only needed regular assets run.
        evidence['packages'].append(record)
    binaries = tools / 'postgresql-16/usr/lib/postgresql/16/bin'
    client = tools / 'postgresql-client-16/usr/lib/postgresql/16/bin'
    library_dir = tools / 'libpq5/usr/lib/x86_64-linux-gnu'
    libraries = list(library_dir.glob('libpq.so.5.*'))
    if len(libraries) != 1: raise RuntimeError('Expected one regular libpq5 implementation')
    alias = library_dir / 'libpq.so.5'
    with alias.open('xb') as stream: stream.write(libraries[0].read_bytes())
    env['LD_LIBRARY_PATH'] = str(library_dir)
    phase = 'toolchain-unavailable'
    run('pg-library-resolution', ['ldd', str(client / 'psql')], env)
    resolution = (results / 'pg-library-resolution.log').read_text()
    if 'libpq.so.5 => ' + str(alias) not in resolution: raise RuntimeError('psql did not resolve verified libpq')
    run('pg-server-version', [str(binaries / 'postgres'), '--version'], env)
    run('pg-client-version', [str(client / 'psql'), '--version'], env)
    phase = 'test-failed'
    cluster = owned / 'cluster'
    run('pg-initdb', [str(binaries / 'initdb'), '-D', str(cluster), '-L', str(tools / 'postgresql-16/usr/share/postgresql/16'),
        '--no-clean', '--auth-local=trust', '--auth-host=trust', '--username=qa07', '--no-locale', '--encoding=UTF8',
        '-c', 'dynamic_shared_memory_type=mmap'], env)
    if 'could not remove shared memory' in (results / 'pg-initdb.log').read_text():
        raise RuntimeError('initdb left POSIX shared-memory cleanup failures')
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
    # All clients use loopback TCP. A long retained workspace can exceed the
    # 107-byte Unix-domain socket pathname limit even when data paths are valid.
    command = [str(binaries / 'postgres'), '-D', str(cluster), '-h', '127.0.0.1', '-p', str(port), '-k', '',
               '-c', 'dynamic_shared_memory_type=mmap']
    def start_server(label):
        global server
        log = (results / (label + '.log')).open('w')
        server = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        log.close()
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if server.poll() is not None: raise RuntimeError('PostgreSQL startup failed; see ' + label + '.log')
            # SQL verifies completed recovery and the intended cluster, not just a listener.
            ready = subprocess.Popen([*sql, 'SELECT 1;'], env={**env, 'PGCONNECT_TIMEOUT': '2'},
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            try:
                if ready.wait(timeout=5) == 0: return
            finally: end_group(ready)
            time.sleep(.1)
        raise TimeoutError('PostgreSQL startup timed out')
    def stop_server():
        global server
        if server:
            if server.poll() is None:
                server.send_signal(signal.SIGINT)
                server.wait(timeout=20)
            if server.returncode != 0: raise RuntimeError('PostgreSQL did not shut down cleanly')
            if live_group(server.pid): raise RuntimeError('PostgreSQL left running children after shutdown')
            remaining = owned_native_processes()
            evidence.setdefault('shutdownProcesses', []).append(remaining)
            if remaining: raise RuntimeError('Task-owned PostgreSQL descendants still active')
        server = None
    sql = [str(client / 'psql'), '-X', '-h', '127.0.0.1', '-p', str(port), '-U', 'qa07', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c']
    start_server('pg-first-start')
    run('pg-write', [*sql, "CREATE TABLE qa07_persistence(id integer PRIMARY KEY, value text NOT NULL); INSERT INTO qa07_persistence VALUES(1,'retained');"], env)
    stop_server()
    start_server('pg-second-start')
    run('pg-read', [*sql, "DO $$ BEGIN IF (SELECT value FROM qa07_persistence WHERE id=1) IS DISTINCT FROM 'retained' THEN RAISE EXCEPTION 'lost committed value'; END IF; END $$;"], env)
    stop_server()
    if '--product-browser' in sys.argv:
        evidence['scope'] = 'Current product migrations and PostgreSQL-backed Development browser with API/database restart'
        start_server('pg-product-start')
        product_env = {**os.environ, 'QA07_POSTGRES': 'true',
            'ConnectionStrings__PostgreSql': f'Host=127.0.0.1;Port={port};Username=qa07;Database=postgres;Pooling=false'}
        with (results / 'backend-browser.log').open('w') as log:
            browser_job = subprocess.Popen(['node', '--import', '../scripts/remote-check/no-delete.mjs', '../scripts/remote-check/browser-backend.mjs'],
                cwd=root / 'frontend', env=product_env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            restarted = False
            deadline = time.monotonic() + 240
            try:
                while browser_job.poll() is None:
                    if (results / 'postgres-restart-request').exists() and not restarted:
                        stop_server()
                        start_server('pg-product-restart')
                        (results / 'postgres-restart-ready').write_text('database restarted')
                        restarted = True
                    if time.monotonic() > deadline: raise TimeoutError('Product PostgreSQL browser timed out')
                    time.sleep(.1)
                evidence['steps'].append({'name': 'product-browser', 'exitCode': browser_job.returncode})
                if browser_job.returncode or not restarted: raise RuntimeError('Product browser did not complete a database restart')
            finally: end_group(browser_job)
        stop_server()
    evidence.update(passed=True, serverMajor=16, retainedCluster=str(cluster), recycledEntries=len(list(recycle.iterdir())))
    phase = 'passed'
except (subprocess.TimeoutExpired, TimeoutError) as error:
    phase = 'test-timeout' if phase == 'test-failed' else 'operation-timeout'
    evidence.update(passed=False, error=str(error))
except Exception as error:
    evidence.update(passed=False, error=str(error))
finally:
    if server:
        try: end_group(server)
        except Exception as error:
            evidence.update(passed=False, cleanupError=str(error))
            phase = 'cleanup-failed'
    # PostgreSQL workers can change sessions. Match the unique task executable
    # path as well as PID/start identity before terminating any remaining native worker.
    survivors = owned_native_processes()
    evidence['cleanupNativeProcesses'] = survivors
    for item in survivors:
        if item in owned_native_processes():
            try: os.kill(item['pid'], signal.SIGKILL)
            except ProcessLookupError: pass
    deadline = time.monotonic() + 5
    while owned_native_processes() and time.monotonic() < deadline: time.sleep(.05)
    evidence['remainingNativeProcesses'] = owned_native_processes()
    if evidence['remainingNativeProcesses']:
        evidence['passed'] = False
        phase = 'cleanup-failed'
    evidence['category'] = phase
    (results / 'postgres-platform.json').write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2))
sys.exit(0 if evidence.get('passed') else 1)
