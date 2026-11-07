"""Run in the user's delegated hierarchy; retain the unit after process exit."""
import json
from pathlib import Path
import subprocess
import sys
import time

root = Path.cwd()
unit = 'ratools-' + root.name + '.service'
command = ['systemd-run', '--user', '--quiet', '--service-type=exec', '--unit=' + unit,
           '--property=RemainAfterExit=yes', '--working-directory=' + str(root),
           'python3', 'scripts/remote-check/no-delete.py', 'python3', 'scripts/remote-check/job.py', sys.argv[1]]
subprocess.run(command, check=True, timeout=20)
owned_group = Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service') / ('ratools-' + root.name)
try:
    deadline = time.monotonic() + 1800
    while time.monotonic() < deadline:
        response = subprocess.run(['systemctl', '--user', 'show', unit,
            '--property=ActiveState,SubState,ExecMainCode,ExecMainStatus,MainPID'], capture_output=True, text=True, check=True, timeout=10)
        state = dict(line.split('=', 1) for line in response.stdout.splitlines() if '=' in line)
        if state.get('MainPID') == '0' and state.get('ExecMainCode') not in {None, '0'}:
            code = int(state['ExecMainStatus']) if state['ExecMainCode'] == '1' else 1
            if owned_group.exists() and 'populated 1' in (owned_group / 'cgroup.events').read_text():
                raise RuntimeError('Exited worker left live PDF resource descendants')
            receipt = root / 'results/manager-launch.json'
            if receipt.parent.exists(): receipt.write_text(json.dumps({'unit': unit, 'state': state, 'exitCode': code}, indent=2))
            sys.exit(code)
        time.sleep(1)
    raise TimeoutError('User manager job exceeded its deadline')
except Exception:
    # Reached only after successful creation of this unique unit. Its tools can
    # have migrated into the task sibling subtree, so terminate both boundaries.
    try:
        subprocess.run(['systemctl', '--user', 'kill', '--kill-whom=all', '--signal=SIGKILL', unit], check=False, timeout=10)
    finally:
        if owned_group.exists(): (owned_group / 'cgroup.kill').write_text('1')
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        response = subprocess.run(['/usr/bin/systemctl', '--user', 'show', unit, '--property=MainPID'],
                                  capture_output=True, text=True, check=True, timeout=10)
        group_empty = not owned_group.exists() or 'populated 1' not in (owned_group / 'cgroup.events').read_text()
        if 'MainPID=0' in response.stdout and group_empty: break
        time.sleep(.1)
    else: raise RuntimeError('Manager failure cleanup did not establish stopped unit and task resources')
    raise
