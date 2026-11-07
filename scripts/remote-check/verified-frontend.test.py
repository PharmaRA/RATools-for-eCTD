"""Changed current input must refuse prior assets before copying them."""
import json
from pathlib import Path
import subprocess
import sys
import zipfile

root = Path.cwd()
fixture = root / 'frontend-mismatch-fixture'; fixture.mkdir()
(fixture / 'results').mkdir(); (fixture / 'frontend').mkdir()
(fixture / 'job-config.json').write_bytes((root / 'job-config.json').read_bytes())
with zipfile.ZipFile(root / 'source.zip') as source, zipfile.ZipFile(fixture / 'source.zip', 'x') as output:
    for name in source.namelist():
        data = source.read(name)
        if name == 'frontend/index.html': data += b'\n<!-- controlled input mismatch -->\n'
        output.writestr(name, data)
result = subprocess.run([sys.executable, str(root / 'scripts/remote-check/verified-frontend.py')],
                        cwd=fixture, capture_output=True, text=True, timeout=20)
assert result.returncode != 0 and 'Frontend input changed; rebuild required: frontend/index.html' in result.stderr, result.stderr
assert not (fixture / 'frontend/dist').exists(), 'Assets copied despite a mismatched source input'
(root / 'results/frontend-mismatch.json').write_text(json.dumps({'passed': True, 'rejectedInput': 'frontend/index.html', 'assetsCopied': False}))
print('PASS changed frontend input rejected before copying cached assets')
