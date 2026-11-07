"""Copy a prior Linux frontend build only when every synchronized input matches."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import zipfile

root = Path.cwd()
binding = json.loads((root / 'job-config.json').read_text(encoding='utf-8-sig'))['verifiedFrontend']
if not re.fullmatch(r'/(data/home|tmp)/ratools-remote/[a-zA-Z0-9_-]+', binding['remote']):
    raise RuntimeError('Frontend evidence root is outside the allowed run roots')
previous = Path(binding['remote'])
for path in [previous, *previous.parents]:
    if path.is_symlink(): raise RuntimeError('Linked evidence root refused')
archive = previous / 'source.zip'
if archive.is_symlink() or hashlib.sha256(archive.read_bytes()).hexdigest() != binding['sourceSha256']:
    raise RuntimeError('Previous frontend source archive does not match the saved run')
summary_path = previous / 'results/summary.json'
if summary_path.is_symlink() or summary_path.parent.is_symlink(): raise RuntimeError('Linked evidence receipt refused')
summary = json.loads(summary_path.read_text())
if summary['sourceSha256'] != binding['sourceSha256']: raise RuntimeError('Build summary source binding differs')
for name in ['dependencies', 'types-app', 'types-config', 'vite', 'budget']:
    matching = [step for step in summary['steps'] if step['name'] == name]
    if len(matching) != 1 or matching[0]['exitCode'] != 0: raise RuntimeError('Previous frontend build gate is missing/failed: ' + name)
with zipfile.ZipFile(archive) as old, zipfile.ZipFile(root / 'source.zip') as current:
    names = sorted(name for name in current.namelist() if name.startswith('frontend/'))
    if names != sorted(name for name in old.namelist() if name.startswith('frontend/')):
        raise RuntimeError('Current frontend source inventory changed; a fresh build is required')
    for name in names:
        if current.read(name) != old.read(name): raise RuntimeError('Frontend input changed; rebuild required: ' + name)
source = previous / 'frontend/dist'
if source.is_symlink() or source.parent.is_symlink(): raise RuntimeError('Linked build directory refused')
destination = root / 'frontend/dist'; destination.mkdir()
assets = []
for path in sorted(source.rglob('*')):
    if path.is_symlink(): raise RuntimeError('Linked frontend asset refused')
    relative = path.relative_to(source)
    if path.is_dir(): (destination / relative).mkdir(parents=True, exist_ok=True)
    elif path.is_file():
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream: stream.write(path.read_bytes())
        assets.append({'path': str(relative), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    else: raise RuntimeError('Special frontend asset refused')
if not any(item['path'] == 'index.html' for item in assets): raise RuntimeError('Frontend entry is absent')
(root / 'results/verified-frontend.json').write_text(json.dumps({
    'sourceRun': str(previous), 'sourceArchive': binding['sourceSha256'], 'matchingInputFiles': len(names), 'assets': assets,
    'scope': 'same-input verified prior Linux frontend build; frontend dependencies/types/Vite/budget not rerun in this job'
}, indent=2))
print('PASS identical frontend inputs and prior build gates; copied retained Linux assets into independent workspace')
