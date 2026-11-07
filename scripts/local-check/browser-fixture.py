from pathlib import Path
import json, hashlib, sys, zipfile
root=Path(__file__).resolve().parents[2]
evidence=Path(sys.argv[1]).resolve();evidence.relative_to(root/'.artifacts/local-check')
workspace=evidence/'input-workspace';workspace.mkdir()
target=b'<?xml version="1.0"?>\n<ectd xmlns:xlink="http://www.w3.org/1999/xlink" dtd-version="3.2"><m2-common-technical-document-summaries/></ectd>'
archive=workspace/'external.zip'
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_STORED) as output:
    output.writestr('bundle/0000/index.xml',target)
    output.writestr('bundle/0001/index.xml',target)
    for index in range(30):output.writestr(f'bundle/0001/INVALID-{index:02}.txt',f'Independent browser fixture {index}\n')
fixture={'archive':str(archive),'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'targetText':target.decode(),
         'sourcePath':'0001/INVALID-00.txt','sourceText':'Independent browser fixture 0\n'}
(evidence/'browser-fixture.json').write_text(json.dumps(fixture,indent=2))
