"""Actual loopback product API, hand-authored external ZIP, no business workspace."""
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import threading
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen
import uuid
import zipfile

root = Path(__file__).resolve().parents[2]
evidence = Path(sys.argv[1]).resolve(); evidence.relative_to(root / '.artifacts/local-check')
backend = evidence / 'backend'
workspace = evidence / 'input-workspace'; workspace.mkdir()
sdk = root / '.artifacts/tools/dotnet-8.0.423'
token = secrets.token_hex(32)  # Task-only authentication; never serialized or printed.
with socket.socket() as reservation:
    reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
base = 'http://127.0.0.1:' + str(port)
env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_NOLOGO': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
       'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': base,
       'ASPNETCORE_HOSTINGSTARTUPASSEMBLIES': 'InputApiStartup', 'Persistence__Provider': 'InMemory',
       'Deployment__Mode': 'LocalOnly', 'Security__ApiKey': token, 'Security__AllowedWorkspaceRoots__0': str(workspace),
       'FileSecrets__ApiKeyPath': '', 'FileSecrets__PostgreSqlPasswordPath': '',
       'FileStorage__RootPath': str(workspace / 'uploads'), 'BackboneOutput__RootPath': str(workspace / 'publish'),
       'BackboneOutput__RetainJobRuns': '0', 'PackageValidation__StorageRoot': str(workspace / 'validation'),
       'PackageValidation__HeartbeatInterval': '00:00:02'}
report = {'scope': 'Windows actual API/InMemory, external synthetic ZIP; not browser/Formal/PostgreSQL qualification',
          'passed': [], 'exitCode': 1, 'facts': {}, 'unrun': ['large multipart disk buffering', 'real browser', 'PostgreSQL restart']}
process = None; collector = None; lines = []


def request(path, body=None, status=200, raw=False, content_type=None):
    headers = {'X-RA-Tools-Api-Key': token}
    data = None
    if body is not None:
        data = body if content_type else json.dumps(body).encode()
        headers['Content-Type'] = content_type or 'application/json'
    try: response = urlopen(Request(base + path, data=data, headers=headers), timeout=15)
    except HTTPError as error: response = error
    payload = response.read()
    if response.status != status: raise AssertionError(f'{path}: expected {status}, actual {response.status}; {payload.decode(errors="replace")}')
    return payload if raw else json.loads(payload)


def upload(application, archive_bytes, status, sequence=None):
    # Stay below ASP.NET's disk-spill threshold; large-body temp cleanup is a separate gate.
    boundary = 'ratools-fixture-' + uuid.uuid4().hex
    fields = {'ApplicationId': application}
    if sequence: fields.update(SequenceNumber=sequence, RootRelativePath='bundle/' + sequence)
    parts = [f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode() for key, value in fields.items()]
    parts += [f'--{boundary}\r\nContent-Disposition: form-data; name="File"; filename="external.zip"\r\nContent-Type: application/zip\r\n\r\n'.encode(),
              archive_bytes, f'\r\n--{boundary}--\r\n'.encode()]
    body = b''.join(parts)
    assert len(body) < 65536, 'Review multipart buffering before increasing this fixture'
    return request('/api/package-validation-inputs/upload', body, status, content_type='multipart/form-data; boundary=' + boundary)


def xml(sequence, count):
    leaves = ''.join(f'<leaf ID="leaf-{sequence}-{i}" operation="new" checksum="{"0" * 32}" checksum-type="md5" xlink:href="m2/missing-{i}.pdf"><title>Independent missing leaf {i}</title></leaf>' for i in range(count))
    return ('<?xml version="1.0"?>\n<!DOCTYPE ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">\n'
            '<ectd xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.2">'
            '<m2-common-technical-document-summaries><m2-2-introduction>' + leaves +
            '</m2-2-introduction></m2-common-technical-document-summaries></ectd>').encode()


def start_api(disable_worker=False):
    global process, collector
    process = subprocess.Popen([str(sdk / 'dotnet.exe'), str(backend / 'RATools.Api.dll'), '--contentRoot', str(backend)],
        cwd=backend, env={**env, 'PackageValidation__Enabled': 'false' if disable_worker else 'true'},
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace', creationflags=subprocess.CREATE_NO_WINDOW)
    def collect():
        for line in process.stdout: lines.append(line.replace(token, '[task-credential-redacted]'))
    collector = threading.Thread(target=collect, daemon=True); collector.start()
    deadline = time.monotonic() + 30
    while True:
        if process.poll() is not None: raise RuntimeError('API exited before readiness; see input-api.log')
        try:
            with urlopen(base + '/health', timeout=1) as response:
                if response.status == 200: break
        except (URLError, TimeoutError): pass
        if time.monotonic() > deadline: raise TimeoutError('API readiness deadline')
        time.sleep(.1)


def stop_api():
    if process is not None and process.poll() is None:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
    if collector is not None: collector.join(timeout=5)


try:
    start_api()
    application = str(uuid.uuid4())
    archive = workspace / 'external.zip'
    target = xml('0001', 26); history = xml('0000', 1)
    with zipfile.ZipFile(archive, 'x') as output:
        output.writestr('bundle/0000/index.xml', history)
        output.writestr('bundle/0001/index.xml', target)
    report['facts']['archiveBefore'] = hashlib.sha256(archive.read_bytes()).hexdigest()
    request_body = {'applicationId': application, 'path': str(archive)}
    ambiguous = request('/api/package-validation-inputs', request_body, 409)
    assert ambiguous['code'] == 'SEQUENCE_SELECTION_REQUIRED'
    candidates = {(item['sequenceNumber'], item['rootRelativePath']) for item in ambiguous['candidates']}
    assert candidates == {('0000', 'bundle/0000'), ('0001', 'bundle/0001')}, candidates
    report['passed'].append('actual external ZIP requires an explicit choice among its two roots')
    archive_bytes = archive.read_bytes()
    upload_error = upload(application, archive_bytes, 409)
    assert upload_error['code'] == 'SEQUENCE_SELECTION_REQUIRED'
    assert {(item['sequenceNumber'], item['rootRelativePath']) for item in upload_error['candidates']} == candidates
    recycled = list((workspace / 'validation/inputs/_recycle').glob('*/package.zip'))
    assert len(recycled) == 1 and recycled[0].read_bytes() == archive_bytes
    assert all(path.name == '_recycle' for path in (workspace / 'validation/inputs').iterdir())
    report['passed'].append('multipart root ambiguity returns 409 and retains the entire failed upload in controlled recycle')
    uploaded = upload(application, archive_bytes, 201, '0001')
    assert uploaded['sequenceNumber'] == '0001' and uploaded['sourceKind'] == 'Zip'
    assert request('/api/package-validation-inputs/' + uploaded['id']) == uploaded
    assert request('/api/package-validation-inputs/' + uploaded['id'] + '/file?' + urlencode({'logicalPath': '0001/index.xml'}), raw=True) == target
    report['facts']['upload'] = {'id': uploaded['id'], 'archiveSha256': hashlib.sha256(archive_bytes).hexdigest(), 'failedUploadRetained': True}
    report['passed'].append('explicit multipart retry registers the chosen root and serves its exact original XML bytes')
    registered = {}
    for sequence in ['0000', '0001']:
        registered[sequence] = request('/api/package-validation-inputs', {**request_body, 'sequenceNumber': sequence, 'rootRelativePath': 'bundle/' + sequence}, 201)
        assert registered[sequence]['sourceKind'] == 'Zip'
        assert request('/api/package-validation-inputs/' + registered[sequence]['id']) == registered[sequence]
    report['facts']['inputs'] = registered
    assert request('/api/applications') == [], 'External registration must not create business workspaces'
    report['passed'].append('explicit ZIP roots register independently without creating business applications')
    profile = 'ich-3.2.2-development-v1'
    assert any(item['profileSnapshotId'] == profile for item in request('/api/package-validations/profiles'))
    body = {'inputId': registered['0001']['id'], 'profileSnapshotId': profile, 'mode': 'Development',
            'history': [{'inputId': registered['0000']['id'], 'profileSnapshotId': profile}], 'idempotencyKey': str(uuid.uuid4())}
    run = request('/api/package-validations', body, 202)
    assert request('/api/package-validations', body, 202)['id'] == run['id']
    deadline = time.monotonic() + 60
    while run['status'] in {'Pending', 'Running'}:
        if time.monotonic() > deadline: raise TimeoutError('Validation completion deadline')
        time.sleep(.1); run = request('/api/package-validations/' + run['id'])
    assert run['status'] == 'Completed' and run['reportAvailable'], run
    url = '/api/package-validations/' + run['id']
    stored = request(url + '/evidence')
    assert stored['history'][0]['inputId'] == registered['0000']['id']
    assert stored['history'][0]['profileSnapshotId'] == profile
    assert stored['historyBaseline']['entries'][0]['trustStatus'] == 'Unverified'
    report['facts']['run'] = run; report['facts']['selection'] = stored['history']
    report['passed'].append('real worker completes explicit history/profile selection and idempotent enqueue; history stays Unverified')
    report_bytes = request(url + '/report', raw=True)
    archived = json.loads(report_bytes); (evidence / 'external-validation-report.json').write_bytes(report_bytes)
    assert archived['isReadyForFinalization'] is False
    findings = []
    first = request(url + '/findings?offset=0&limit=5'); total = first['totalCount']
    assert total > 5, total
    for offset in range(0, total, 5):
        page = first if offset == 0 else request(url + f'/findings?offset={offset}&limit=5')
        assert page['totalCount'] == total and page['offset'] == offset
        findings.extend(page['items'])
    assert len(findings) == total
    assert findings == [item for rule in archived['coverage'] for item in rule['findings']], 'Paged findings differ from archived report'
    report['facts']['findings'] = total
    report['facts']['report'] = {'length': len(report_bytes), 'sha256': hashlib.sha256(report_bytes).hexdigest()}
    report['passed'].append('multiple findings pages exactly match the downloaded immutable report; Development is not ready')
    input_url = '/api/package-validation-inputs/' + registered['0001']['id']
    query = urlencode({'logicalPath': '0001/index.xml'})
    preview = request(input_url + '/text?' + query)
    assert preview['sha256'] == hashlib.sha256(target).hexdigest()
    assert 'Independent missing leaf' in '\n'.join(preview['lines'])
    assert request(input_url + '/file?' + query, raw=True) == target
    report['passed'].append('source text location and downloaded bytes resolve to the selected external ZIP root')
    with zipfile.ZipFile(archive, 'a') as output: output.writestr('bundle/0001/changed.txt', 'controlled source mutation')
    changed = request(input_url + '/text?' + query, status=409)
    assert changed['code'] == 'INPUT_CHANGED'
    assert request(url + '/report', raw=True) == report_bytes
    report['passed'].append('changed source refuses old location reads while the archived report remains byte-identical')
    active_archive = workspace / 'active-cancel.zip'
    with zipfile.ZipFile(active_archive, 'x', compression=zipfile.ZIP_STORED) as output:
        output.writestr('bundle/0001/index.xml', target)
        payload = b'controlled active-cancellation input\n' * 8192
        for index in range(8):
            with output.open(f'bundle/0001/payload-{index}.bin', 'w') as member:
                for _ in range(32): member.write(payload)
    with active_archive.open('rb') as stream: active_digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    active_input = request('/api/package-validation-inputs', {'applicationId': application, 'path': str(active_archive),
        'sequenceNumber': '0001', 'rootRelativePath': 'bundle/0001'}, 201)
    active = request('/api/package-validations', {'inputId': active_input['id'], 'profileSnapshotId': profile,
        'mode': 'Development', 'idempotencyKey': str(uuid.uuid4())}, 202)
    active_url = '/api/package-validations/' + active['id']
    deadline = time.monotonic() + 30
    while active['status'] == 'Pending':
        if time.monotonic() > deadline: raise TimeoutError('Worker claim deadline')
        time.sleep(.005); active = request(active_url)
    assert active['status'] == 'Running' and active['attemptCount'] == 1 and active['startedUtc'], active
    observed = {key: active[key] for key in ['status', 'stage', 'attemptCount', 'startedUtc']}
    report['facts']['activeCancellation'] = {'observedBeforeCancel': observed,
        'archiveBytes': active_archive.stat().st_size, 'archiveSha256': active_digest, 'heartbeatMilliseconds': 2000}
    acknowledged = request(active_url + '/cancel', {}, 200)
    assert acknowledged['cancellationRequested'], acknowledged
    deadline = time.monotonic() + 30
    while acknowledged['status'] in {'Pending', 'Running'}:
        if time.monotonic() > deadline: raise TimeoutError('Active cancellation completion deadline')
        time.sleep(.02); acknowledged = request(active_url)
    assert acknowledged['status'] == 'Cancelled' and acknowledged['failureCode'] == 'CANCELLED'
    assert acknowledged['attemptCount'] == 1 and not acknowledged['reportAvailable']
    report['facts']['activeCancellation']['terminal'] = acknowledged
    request(active_url + '/report', status=410)
    assert request(active_url + '/findings', status=409)['stage'] == 'Cancelled'
    report['passed'].append('actual worker Running attempt1 is cancelled; report410 and findings409 refuse completed evidence')
    recovery = request('/api/package-validations', {'inputId': uploaded['id'], 'profileSnapshotId': profile,
        'mode': 'Development', 'idempotencyKey': str(uuid.uuid4())}, 202)
    deadline = time.monotonic() + 30
    while recovery['status'] in {'Pending', 'Running'}:
        if time.monotonic() > deadline: raise TimeoutError('Worker recovery completion deadline')
        time.sleep(.05); recovery = request('/api/package-validations/' + recovery['id'])
    assert recovery['status'] == 'Completed' and recovery['attemptCount'] == 1 and recovery['reportAvailable'], recovery
    assert request(active_url) == acknowledged, 'Cancelled terminal was overwritten by late work'
    request(active_url + '/report', status=410)
    with active_archive.open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest() == active_digest
    report['facts']['afterCancellation'] = recovery
    report['passed'].append('same sequential worker completes its next run; cancelled state and original archive bytes remain unchanged')
    stop_api()
    start_api(disable_worker=True)
    cancel_input = request('/api/package-validation-inputs', {**request_body, 'sequenceNumber': '0001', 'rootRelativePath': 'bundle/0001'}, 201)
    pending = request('/api/package-validations', {'inputId': cancel_input['id'], 'profileSnapshotId': profile, 'mode': 'Development', 'idempotencyKey': str(uuid.uuid4())}, 202)
    assert pending['status'] == 'Pending' and pending['attemptCount'] == 0
    cancel_url = '/api/package-validations/' + pending['id']
    cancelled = request(cancel_url + '/cancel', {}, 200)
    assert cancelled['status'] == 'Cancelled' and cancelled['attemptCount'] == 0 and not cancelled['reportAvailable']
    assert request(cancel_url)['status'] == 'Cancelled'
    request(cancel_url + '/report', status=410)
    report['facts']['cancellation'] = {'status': cancelled['status'], 'attemptCount': cancelled['attemptCount'], 'scope': 'separate InMemory API process with worker disabled; Pending cancellation only'}
    report['passed'].append('pending cancellation reaches Cancelled with zero worker attempts and no fabricated report')
    report['exitCode'] = 0
except Exception as error:
    report['error'] = str(error)
finally:
    stop_api()
    (evidence / 'input-api.log').write_text(''.join(lines), encoding='utf-8')
    report['apiStopped'] = process is None or process.poll() is not None
    (evidence / 'input-api.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({key: report[key] for key in ['scope', 'passed', 'exitCode', 'apiStopped']}, indent=2))
sys.exit(report['exitCode'])
