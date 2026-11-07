"""Verify downloaded bytes with Python's ZIP reader, independently of product code."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

results = Path('results')
report = json.loads((results / 'backend-browser.json').read_text())
assert not report['errors'], 'Browser did not complete successfully'
facts = report['facts']
expected = (results / 'synthetic.pdf').read_bytes()
assert hashlib.sha256(expected).hexdigest() == facts['pdf']['sha256']
archive = Path(facts['download']['retainedPath'])
assert hashlib.sha256(archive.read_bytes()).hexdigest() == facts['download']['sha256']
with zipfile.ZipFile(archive) as package:
    pdfs = [name for name in package.namelist() if name.lower().endswith('.pdf')]
    assert len(pdfs) == 1, pdfs
    assert package.read(pdfs[0]) == expected, 'Downloaded PDF differs from the uploaded fixture'
    xml = ET.fromstring(package.read('0000/index.xml'))
    # ICH 3.2.2 uses this legacy namespace; a separate US regional XML leaf is valid.
    href_key = '{http://www.w3c.org/1999/xlink}href'
    leaves = [item for item in xml.iter() if item.tag.split('}')[-1] == 'leaf'
              and item.attrib.get(href_key, '').lower().endswith('.pdf')]
    assert len(leaves) == 1, 'Backbone must contain exactly one PDF leaf'
    href = leaves[0].attrib[href_key]
    assert '0000/' + href == pdfs[0], 'Backbone points to a different PDF'
    assert leaves[0].attrib['checksum'] == hashlib.md5(expected).hexdigest()
    evidence = {'passed': 3, 'failed': 0, 'skipped': 0, 'pdfPath': pdfs[0],
                'pdfLength': len(expected), 'pdfSha256': facts['pdf']['sha256'],
                'checks': ['browser ZIP identity', 'exact original PDF bytes', 'backbone href and MD5']}
(results / 'download-pdf.json').write_text(json.dumps(evidence, indent=2))
print(json.dumps(evidence))
