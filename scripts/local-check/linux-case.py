"""Dispatch the bounded Linux hash-cache probe to the existing QA machine."""
import base64
import datetime
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import uuid
import zipfile

root = Path(__file__).resolve().parents[2]
machine = 'm_agquz43gf246rsedwy3bkathci'
cli = root / '.artifacts/tools/instabox-0.8.196/package/cli/instabox-windows-amd64.exe'
run = 'linux-case-' + datetime.datetime.now().strftime('%Y%m%dT%H%M%S') + '-' + uuid.uuid4().hex
out = root / '.artifacts/local-check' / run
remote = '/tmp/ratools-qa07-' + run
env = {**os.environ, 'INSTABOX_NO_UPDATE_CHECK': '1'}


def refuse_links(path):
    for ancestor in (path, *path.parents):
        if ancestor.exists() and os.lstat(ancestor).st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise RuntimeError('Input/output ancestor must not be a reparse point')


def invoke(arguments, timeout=35):
    process = subprocess.run([str(cli), *arguments], env=env, capture_output=True, timeout=timeout)
    try:
        data = json.loads(process.stdout)
    except ValueError:
        data = None
    return process.returncode, data


refuse_links(out)
out.mkdir()
print('Linux case evidence: ' + str(out), flush=True)
files = {p: root / p for p in (
    'src/RATools.Application/Applications/ImportFileHashCache.cs',
    'tests/RATools.Tests/Applications/ImportFileHashCacheTests.cs',
    'scripts/remote-check/ImportCaseProbe.cs', 'scripts/remote-check/import-case.py',
    'scripts/remote-check/no-delete.py')}
assets = json.loads((root / 'tests/RATools.Tests/obj/project.assets.json').read_text(encoding='utf-8-sig'))
for key, relative in (
    ('xunit.assert/2.9.2', 'lib/net6.0/xunit.assert.dll'),
    ('xunit.extensibility.core/2.9.2', 'lib/netstandard1.1/xunit.core.dll'),
    ('xunit.abstractions/2.0.3', 'lib/netstandard2.0/xunit.abstractions.dll')):
    paths = [Path(folder) / assets['libraries'][key]['path'] / relative for folder in assets['packageFolders']]
    files[Path(relative).name] = next(path for path in paths if path.is_file())
for path in files.values():
    refuse_links(path)
manifest = {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in files.items()}
(out / 'source-manifest.json').write_text(json.dumps(manifest, indent=2))
archive = out / 'source.zip'
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as package:
    for name, path in files.items():
        package.writestr(name, path.read_bytes())
    package.writestr('source-manifest.json', (out / 'source-manifest.json').read_bytes())
state = {'remote': remote, 'machine': machine, 'sourceSha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
         'bytes': archive.stat().st_size, 'launcherSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
         'guardedCommand': ['python3', '-B', 'scripts/remote-check/no-delete.py',
                            'python3', '-B', 'scripts/remote-check/import-case.py']}
code = '''import hashlib,os,pathlib,zipfile
root=pathlib.Path(%r); os.chdir(root)
archive=root/'source.zip'
assert hashlib.sha256(archive.read_bytes()).hexdigest()==%r
with zipfile.ZipFile(archive) as package:
 for name in package.namelist():
  path=pathlib.PurePosixPath(name)
  assert not path.is_absolute() and '..' not in path.parts
  target=root/name; target.parent.mkdir(parents=True,exist_ok=True)
  with target.open('xb') as stream: stream.write(package.read(name))
os.execvp('python3',%r)
''' % (remote, state['sourceSha256'], state['guardedCommand'])
command = "python3 -B -c 'import base64;exec(base64.b64decode(\"" + base64.b64encode(code.encode()).decode() + "\"))'"
state['launchCommand'] = command
(out / 'run.json').write_text(json.dumps(state, indent=2))
upload_exit, _ = invoke(['cp', '-p', str(archive), machine + ':' + remote + '/source.zip', '--json'])
if upload_exit:
    raise RuntimeError('Upload CLI exit ' + str(upload_exit))
cli_exit, execution = invoke(['exec', machine, '--json', '--timeout', '150', '--', command], 180)
receipt = {'cliExitCode': cli_exit}
if isinstance(execution, dict):
    receipt.update({key: execution[key] for key in ('exitCode', 'timedOut', 'truncated', 'oomKilled', 'durationMs') if key in execution})
(out / 'execution.json').write_text(json.dumps(receipt, indent=2))
retrievals = {}
for name in ('summary.json', 'tests.json', 'compile.log', 'test.log'):
    retrievals[name], _ = invoke(['cp', machine + ':' + remote + '/' + name, str(out / name), '--json'])
(out / 'retrievals.json').write_text(json.dumps(retrievals, indent=2))
if not (out / 'summary.json').exists():
    raise RuntimeError('Remote summary unavailable; see execution/retrieval receipts')
summary = json.loads((out / 'summary.json').read_text())
print(json.dumps(summary, indent=2), flush=True)
passed = (cli_exit == 0 and receipt.get('exitCode') == 0 and not receipt.get('timedOut', True)
          and not receipt.get('truncated', True) and not receipt.get('oomKilled', True)
          and all(value == 0 for value in retrievals.values()) and summary.get('exitCode') == 0
          and summary.get('sourceManifestSha256') == hashlib.sha256((out / 'source-manifest.json').read_bytes()).hexdigest())
raise SystemExit(0 if passed else 1)
