"""Actual user-manager polling failure must stop migrated task children."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

root = Path.cwd()
fixture = root / ('manager-fault-' + uuid.uuid4().hex)
scripts = fixture / 'scripts/remote-check'
scripts.mkdir(parents=True)
(fixture / 'results').mkdir()
for name in ['no-delete.py', 'manager-launch.py']:
    shutil.copyfile(root / 'scripts/remote-check' / name, scripts / name)
worker = '''import os,subprocess,sys,time
from pathlib import Path
r=Path.cwd(); g=Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service')/('ratools-'+r.name)
g.mkdir(); child=g/'child'; child.mkdir()
code="import os,time;from pathlib import Path;Path(%r).write_text(str(os.getpid()));Path(%r).write_text('ready');time.sleep(30)" % (str(child/'cgroup.procs'),str(r/'ready'))
subprocess.Popen([sys.executable,'-c',code])
time.sleep(30)
'''
(scripts / 'job.py').write_text(worker)
fakebin = fixture / 'fakebin'; fakebin.mkdir()
shim = fakebin / 'systemctl'
shim.write_text('#!/usr/bin/python3\nimport os,sys\nfrom pathlib import Path\n'
               f'if "show" in sys.argv and Path({str(fixture / "ready")!r}).exists(): sys.exit(5)\n'
               'os.execv("/usr/bin/systemctl",["systemctl",*sys.argv[1:]])\n')
shim.chmod(0o755)
with (root / 'results/manager-fault.log').open('w') as log:
    result = subprocess.run([sys.executable, str(scripts / 'manager-launch.py'), 'test-only-digest'], cwd=fixture,
                            env={**os.environ, 'PATH': str(fakebin) + ':' + os.environ['PATH']},
                            stdout=log, stderr=subprocess.STDOUT, timeout=50)
assert result.returncode != 0, 'Polling failure was incorrectly reported as successful'
assert (fixture / 'ready').exists(), 'Migrated child never ran'
group = Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service') / ('ratools-' + fixture.name)
assert 'populated 0' in (group / 'cgroup.events').read_text(), 'Migrated child remains active'
state = subprocess.check_output(['/usr/bin/systemctl', '--user', 'show', 'ratools-' + fixture.name + '.service', '--property=MainPID'], text=True)
assert state.strip() == 'MainPID=0', 'Worker remains active'
(root / 'results/manager-fault.json').write_text(json.dumps({'passed': True, 'injectedPollExit': 5, 'workerStopped': True, 'resourceGroupEmpty': True, 'retainedFixture': str(fixture)}))
print('PASS actual polling failure stops unit and migrated resource child; evidence retained')
