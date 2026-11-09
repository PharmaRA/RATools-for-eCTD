"""Retained user-manager service with independent runtime deadline and group stop."""
import json
import os
from pathlib import Path
import subprocess
import sys
import time

root = Path.cwd()
unit = 'ratools-wpg-' + root.name + '.service'
query = subprocess.run(['systemctl', '--user', 'show', unit, '--property=LoadState'],
                       capture_output=True, text=True, timeout=10)
if query.stdout.strip() != 'LoadState=not-found':
    raise RuntimeError('Existing service name refused')
result = {'unit': unit, 'exitCode': 1}
try:
    subprocess.run(['systemd-run', '--user', '--quiet', '--service-type=exec', '--unit=' + unit,
        '--property=RemainAfterExit=yes', '--property=RuntimeMaxSec=450', '--property=TimeoutStopSec=40',
        '--property=KillMode=mixed', '--working-directory=' + str(root),
        'python3', str(root / 'no-delete.py'), 'python3', str(root / 'windows-postgres-service.py')], check=True, timeout=20)
    deadline = time.monotonic() + 510
    while time.monotonic() < deadline:
        response = subprocess.run(['systemctl', '--user', 'show', unit,
            '--property=MainPID,ExecMainCode,ExecMainStatus,ControlGroup,ActiveState,Result'], capture_output=True, text=True, check=True, timeout=10)
        state = dict(line.split('=', 1) for line in response.stdout.splitlines() if '=' in line)
        if state.get('MainPID') == '0' and state.get('ExecMainCode') not in {None, '0'}:
            group = Path('/sys/fs/cgroup') / state.get('ControlGroup', '').lstrip('/')
            if state.get('ControlGroup') and group.exists() and 'populated 1' in (group / 'cgroup.events').read_text():
                raise RuntimeError('Exited service retains live processes')
            result.update(exitCode=int(state['ExecMainStatus']) if state['ExecMainCode'] == '1' else 1, state=state)
            service = json.loads((root / 'results/summary.json').read_text())
            if state.get('Result') != 'success' or service.get('exitCode') != 0 or service.get('stopReason') != 'owned-marker':
                result['exitCode'] = 1
            break
        time.sleep(.2)
    else:
        raise TimeoutError('Retained PG manager exceeded deadline')
except Exception as error:
    result['error'] = type(error).__name__
    subprocess.run(['systemctl', '--user', 'kill', '--kill-whom=all', '--signal=SIGKILL', unit], check=False, timeout=10)
finally:
    deadline = time.monotonic() + 15
    empty_observations = 0
    while time.monotonic() < deadline and empty_observations < 2:
        response = subprocess.run(['systemctl', '--user', 'show', unit, '--property=MainPID,ControlGroup'],
                                  capture_output=True, text=True, timeout=10)
        state = dict(line.split('=', 1) for line in response.stdout.splitlines() if '=' in line)
        group = Path('/sys/fs/cgroup') / state.get('ControlGroup', '').lstrip('/')
        stopped = state.get('MainPID') == '0' and (not state.get('ControlGroup') or not group.exists() or 'populated 1' not in (group / 'cgroup.events').read_text())
        empty_observations = empty_observations + 1 if stopped else 0
        time.sleep(.1)
    result['stopped'] = empty_observations == 2
    if not result['stopped']:
        result['exitCode'] = 1
    (root / 'manager.json').write_text(json.dumps(result, indent=2))
sys.exit(result['exitCode'])
