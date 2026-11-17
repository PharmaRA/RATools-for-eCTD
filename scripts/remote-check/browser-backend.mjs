// Real product API and production UI; synthetic one-page PDF, Development only.
import assert from 'node:assert/strict'
import { randomBytes, randomUUID, createHash } from 'node:crypto'
import { readFile, writeFile, mkdir, mkdtemp } from 'node:fs/promises'
import { resolve, basename } from 'node:path'
import { startProcess, fetchDeadline as fetch } from './browser-process.mjs'

const externalFixturePython = String.raw`"""Deterministic external ZIP for real UI validation; retain every fixture byte."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

workspace = Path(sys.argv[1])
archive = workspace / 'external-browser.zip'
source_path = '0001/m2/UPPER0.txt'
source_text = 'Independent browser fixture 0\n'


def xml(sequence, count):
    leaves = []
    for index in range(count):
        content = f'Independent browser fixture {index}\n'.encode()
        digest = hashlib.md5(content).hexdigest()
        leaves.append(f'<leaf ID="leaf-{sequence}-{index}" operation="new" checksum="{digest}" '
                      f'checksum-type="md5" xlink:href="m2/UPPER{index}.txt"><title>External text {index}</title></leaf>')
    return ('<?xml version="1.0"?>\n<!DOCTYPE ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">\n'
            '<ectd xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.2">'
            '<m2-common-technical-document-summaries><m2-2-introduction>' + ''.join(leaves) +
            '</m2-2-introduction></m2-common-technical-document-summaries></ectd>').encode()


with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_STORED) as output:
    for sequence, count in [('0000', 1), ('0001', 30)]:
        index_xml = xml(sequence, count)
        output.writestr(f'bundle/{sequence}/index.xml', index_xml)
        output.writestr(f'bundle/{sequence}/index-md5.txt', hashlib.md5(index_xml).hexdigest() + '\n')
        for index in range(count):
            output.writestr(f'bundle/{sequence}/m2/UPPER{index}.txt', f'Independent browser fixture {index}\n')

fixture = {'archive': str(archive), 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
           'sourcePath': source_path, 'sourceText': source_text}
Path(sys.argv[2]).write_text(json.dumps(fixture, indent=2), encoding='utf-8')
`

async function exerciseExternalInputs({ root, results, workspace, profile, base, send, evaluate, request, requestBytes,
  until, stop, stopApi, startApi, downloadEvents, passed, facts }) {
  const generation = startProcess('/usr/bin/python3', ['-c', externalFixturePython,
    workspace, `${results}/external-browser-fixture.json`], { stdio: ['ignore', 'pipe', 'pipe'] })
  let generationLog = ''
  generation.process.stdout.on('data', data => { generationLog += data })
  generation.process.stderr.on('data', data => { generationLog += data })
  try {
    await generation.ready
    await until(() => generation.process.exitCode !== null || generation.process.signalCode !== null, 'external ZIP fixture created')
    assert.equal(generation.process.exitCode, 0, generationLog)
  } finally { await stop(generation.process) }
  const fixture = JSON.parse(await readFile(`${results}/external-browser-fixture.json`, 'utf8'))
  const external = facts.externalInputs = { fixture, downloads: [] }
  const wait = expression => until(() => evaluate(expression), expression)
  const click = async label => {
    const button = `[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled)`
    await wait(`!!(${button})`); await evaluate(`(${button}).click()`)
  }
  const choose = async (selector, label) => {
    const input = `(()=>{const e=document.querySelector(${JSON.stringify(selector)});return e.matches('input')?e:e.querySelector('input[role="combobox"]')})()`
    const box = await evaluate(`(()=>{const e=${input};e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`)
    await send('Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', clickCount: 1, ...box })
    await send('Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', clickCount: 1, ...box })
    const dropdown = `document.getElementById((${input}).getAttribute('aria-controls'))?.closest('.ant-select-dropdown')`
    await wait(`(${input}).getAttribute('aria-expanded')==='true'&&[...((${dropdown})?.querySelectorAll('.ant-select-item-option')||[])].some(e=>e.textContent.trim()===${JSON.stringify(label)})`)
    await evaluate(`[...(${dropdown}).querySelectorAll('.ant-select-item-option')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).click()`)
    await wait(`document.querySelector(${JSON.stringify(selector)}).closest('.ant-select').textContent.includes(${JSON.stringify(label)})`)
  }
  const register = async (role, sequence) => {
    await click(role); await wait(`!!document.querySelector('#validation-zip')`)
    const document = await send('DOM.getDocument')
    const node = await send('DOM.querySelector', { nodeId: document.root.nodeId, selector: '#validation-zip' })
    await send('DOM.setFileInputFiles', { nodeId: node.nodeId, files: [fixture.archive] })
    await click('登记并选择'); await wait(`!!document.querySelector('#validation-root')`)
    await choose('#validation-root', `${sequence} / bundle/${sequence}`)
    await click('登记并选择'); await wait(`!document.querySelector('#validation-zip')`)
  }
  const nativeDownload = async (label, fileName, expected) => {
    assert.equal(basename(fileName), fileName)
    const before = downloadEvents.length
    await click(label)
    let begun
    await until(() => {
      begun = downloadEvents.slice(before).find(event => event.method === 'Browser.downloadWillBegin' && event.suggestedFilename === fileName)
      return !!begun
    }, `native download started: ${fileName}`)
    await until(() => downloadEvents.slice(before).some(event => event.guid === begun.guid && event.state === 'completed'), `native download completed: ${fileName}`)
    const bytes = await readFile(`${profile}/downloads/${fileName}`)
    assert.deepEqual(bytes, expected, `Native download bytes differ: ${fileName}`)
    external.downloads.push({ fileName, guid: begun.guid, length: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') })
    return bytes
  }
  const applicationsBefore = (await request('/api/applications')).map(app => app.id).sort()
  await send('Page.navigate', { url: `${base}/package-validations` })
  await wait(`!!document.querySelector('#validation-application')`)
  external.applicationId = await evaluate(`document.querySelector('#validation-application').value`)
  assert.ok(!applicationsBefore.includes(external.applicationId))
  await register('选择目标输入', '0001')
  await register('添加历史序列', '0000')
  const selectedProfile = 'ich-3.2.2-development-v1'
  await choose('#validation-profile', selectedProfile)
  await choose('[aria-label="历史 0000 规则范围"]', selectedProfile)
  await click('开始独立校验')
  await wait(`document.body.innerText.includes('运行完成（不等于通过）')&&document.body.innerText.includes('共 ')`)
  const runId = await evaluate(`new URL(location.href).searchParams.get('run')`)
  const run = await request(`/api/package-validations/${runId}`), binding = await request(`/api/package-validations/${runId}/evidence`)
  assert.equal(run.status, 'Completed'); assert.equal(binding.history.length, 1)
  assert.equal(binding.historyBaseline.entries[0].trustStatus, 'Unverified')
  assert.equal(binding.history[0].profileSnapshotId, selectedProfile); assert.equal(run.binding.profileSnapshotId, selectedProfile)
  assert.deepEqual((await request('/api/applications')).map(app => app.id).sort(), applicationsBefore)
  external.run = run; external.history = binding.history
  passed.push('external ZIP UI selects target/history roots and explicit profiles; real worker completes without creating a business application')
  const first = await request(`/api/package-validations/${runId}/findings?offset=0&limit=20`)
  assert.ok(first.totalCount > 20, 'External fixture must exercise actual findings pagination')
  const verifyPage = async (page, data) => {
    const expected = data.items.map(item => ({ code: item.code, path: item.location.logicalPath || item.location.nodePath || '规则级问题' }))
    await wait(`(()=>{const table=[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位'));if(!table||table.querySelector('.ant-spin-spinning')||table.querySelector('.ant-pagination-item-active')?.textContent!==${JSON.stringify(String(page))})return false;const rows=[...table.querySelectorAll('tbody tr.ant-table-row')];const expected=${JSON.stringify(expected)};return rows.length===expected.length&&expected.every((item,index)=>rows[index].innerText.includes(item.code)&&rows[index].innerText.includes(item.path))})()`)
  }
  await verifyPage(1, first)
  await evaluate(`[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位')).querySelector('.ant-pagination-next button').click()`)
  const second = await request(`/api/package-validations/${runId}/findings?offset=20&limit=20`)
  await verifyPage(2, second)
  external.findings = first.totalCount
  passed.push('external findings second UI page matches archived API rows')
  await evaluate(`[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位')).querySelector('.ant-pagination-prev button').click()`)
  let found = false
  for (let page = 1; page <= Math.ceil(first.totalCount / 20); page++) {
    const data = page === 1 ? first : page === 2 ? second : await request(`/api/package-validations/${runId}/findings?offset=${(page - 1) * 20}&limit=20`)
    await verifyPage(page, data)
    found = await evaluate(`[...document.querySelectorAll('tr')].some(e=>e.innerText.includes(${JSON.stringify(fixture.sourcePath)})&&e.innerText.includes('查看定位'))`)
    if (found) break
    if (page < Math.ceil(first.totalCount / 20)) await evaluate(`[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位')).querySelector('.ant-pagination-next button').click()`)
  }
  assert.ok(found, 'Known delivered source must have a visible finding')
  await evaluate(`[...document.querySelectorAll('tr')].find(e=>e.innerText.includes(${JSON.stringify(fixture.sourcePath)})&&e.innerText.includes('查看定位')).querySelector('button').click()`)
  await wait(`document.querySelector('[data-testid="validation-source-text"]')?.textContent.includes('Independent browser fixture 0')`)
  await nativeDownload('下载定位文件原始字节', 'UPPER0.txt', Buffer.from(fixture.sourceText))
  await evaluate(`document.querySelector('.ant-drawer-close').click()`)
  passed.push('external finding displays exact source text and native source download completes with original bytes')
  const expectedReport = await requestBytes(`/api/package-validations/${runId}/report`)
  const reportName = `validation-${runId}-report.json`
  const reportBytes = await nativeDownload('下载完整报告 JSON', reportName, expectedReport)
  assert.equal(JSON.parse(reportBytes.toString('utf8')).isReadyForFinalization, false)
  await writeFile(`${results}/external-browser-report.json`, reportBytes)
  passed.push('external full report native download completes and matches archived report; Development remains not ready')
  await stopApi(); await startApi(true)
  await send('Page.navigate', { url: `${base}/package-validations` })
  await wait(`!!document.querySelector('#validation-application')`)
  await register('选择目标输入', '0001'); await choose('#validation-profile', selectedProfile)
  await click('开始独立校验')
  await wait(`[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='请求取消')`)
  const cancelId = await evaluate(`new URL(location.href).searchParams.get('run')`)
  const beforeCancel = await request(`/api/package-validations/${cancelId}`)
  assert.equal(beforeCancel.status, 'Pending'); assert.equal(beforeCancel.attemptCount, 0)
  await click('请求取消')
  await wait(`document.body.innerText.includes('已取消')&&![...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='请求取消')`)
  external.cancelled = await request(`/api/package-validations/${cancelId}`)
  assert.equal(external.cancelled.status, 'Cancelled'); assert.equal(external.cancelled.attemptCount, 0)
  assert.equal(external.cancelled.reportAvailable, false)
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='下载完整报告 JSON').disabled`), true)
  assert.equal(createHash('sha256').update(await readFile(fixture.archive)).digest('hex'), fixture.sha256)
  passed.push('external UI cancels Pending run in worker-disabled API; no report is fabricated and original ZIP is unchanged')
}

async function exerciseRegionalForm({ id, base, send, evaluate, request, until, results, passed, facts, readBytes }) {
  const sequence = `/api/applications/${id}/sequences/0000`
  const before = await request(`${sequence}/publishing-metadata`)
  const captured = facts.regionalForm = { beforeRevision: before.regional.id, standardsSnapshotId: before.regional.standardsSnapshotId }
  const wait = expression => until(() => evaluate(expression), expression)
  const panel = `document.querySelector('[aria-label="区域元数据编辑"]')`
  const control = path => `${panel}.querySelector('input[aria-label=${JSON.stringify(path)}]')`
  const click = async label => {
    const button = `[...${panel}.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled)`
    await wait(`!!(${button})`); await evaluate(`(${button}).click()`)
  }
  const input = async (path, value) => {
    await wait(`!!(${control(path)})&&!(${control(path)}).disabled`)
    await evaluate(`(()=>{const e=${control(path)};Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,${JSON.stringify(value)});e.dispatchEvent(new Event('input',{bubbles:true}))})()`)
    await wait(`(${control(path)}).value===${JSON.stringify(value)}`)
  }
  const options = path => evaluate(`[...(${control(path)}).list.options].map(option=>option.value)`)
  await send('Page.navigate', { url: `${base}/applications/${id}/sequences/0000/workspace` })
  await wait(`!!(${panel})`); await click('读取区域元数据')
  await wait(`!!(${control('us.applicationType')})`)
  const applicationCodes = await options('us.applicationType')
  assert.equal(applicationCodes.length, 6); assert.ok(applicationCodes.includes('fdaat1')); assert.ok(applicationCodes.includes('fdaat6'))
  assert.ok(!applicationCodes.includes('fdaat8'))
  assert.ok((await options('us.contacts[0].type')).includes('fdaact1'))
  assert.ok(!(await options('us.contacts[0].type')).includes('regulatory'))
  assert.ok((await options('us.contacts[0].phones[0].type')).includes('fdatnt1'))
  await input('us.contacts[0].type', 'regulatory'); await input('us.contacts[0].phones[0].type', 'business')
  await click('预检当前输入')
  for (const path of ['us.contacts[0].type', 'us.contacts[0].phones[0].type']) {
    await wait(`[...(document.querySelector('[aria-label="区域元数据预检"]')?.querySelectorAll('p button')||[])].some(e=>e.textContent===${JSON.stringify(path)})`)
  }
  assert.equal(await evaluate(`(${control('us.contacts[0].type')}).value`), 'regulatory')
  assert.equal(await evaluate(`(${control('us.contacts[0].phones[0].type')}).value`), 'business')
  passed.push('regional UI offers archived official dictionaries and preflight locates invalid aliases without discarding input')
  await input('us.contacts[0].type', 'fdaact1'); await input('us.contacts[0].phones[0].type', 'fdatnt1')
  await input('us.applicationType', 'fdaat1'); await input('us.submissionType', 'fdast3')
  assert.deepEqual(await options('us.supplementEffectiveDateType'), ['fdasedt1', 'fdasedt2', 'fdasedt3'])
  await input('us.supplementEffectiveDateType', 'fdasedt3')
  await input('us.submissionType', 'fdast2')
  assert.deepEqual(await options('us.supplementEffectiveDateType'), ['fdasedt1'])
  assert.equal(await evaluate(`(${control('us.supplementEffectiveDateType')}).value`), 'fdasedt3')
  await wait(`${panel}.querySelector('[data-field-path="us.supplementEffectiveDateType"]').textContent.includes('当前组合不支持此值，已保留')`)
  const save = async () => { await click('保存区域草稿'); await wait(`${panel}.textContent.includes('区域草稿已保存。')`) }
  await save()
  const invalid = await request(`${sequence}/publishing-metadata`)
  assert.equal(invalid.regional.content.us.submissionType, 'fdast2')
  assert.equal(invalid.regional.content.us.supplementEffectiveDateType, 'fdasedt3')
  assert.ok(invalid.regional.diagnostics.some(item => item.code === 'SupplementEffectiveTypeCombinationInvalid'))
  const blocked = await request('/api/backbone/generate', { applicationId: id, sequenceNumber: '0000' }, 400)
  assert.equal(blocked.fieldPath, 'us.supplementEffectiveDateType')
  captured.invalidRevision = invalid.regional.id
  passed.push('regional UI retains unsupported effective type after changing supplement kind; saved draft cannot generate XML')
  await input('us.supplementEffectiveDateType', 'fdasedt1'); await save()
  const saved = await request(`${sequence}/publishing-metadata`)
  const expected = structuredClone(before.regional.content)
  Object.assign(expected.us, { applicationType: 'fdaat1', submissionType: 'fdast2', supplementEffectiveDateType: 'fdasedt1' })
  assert.deepEqual(saved.regional.content, expected, 'Unedited regional fields or collection identities changed')
  assert.deepEqual(saved.regional.nodeContexts, before.regional.nodeContexts)
  assert.deepEqual(saved.regional.documentContexts, before.regional.documentContexts)
  for (const field of ['applicationType', 'submissionType', 'submissionSubtype', 'sequenceDescription', 'applicantName', 'formType',
    'applicantContactName', 'applicantContactType', 'telephone', 'telephoneNumberType', 'email']) assert.deepEqual(saved[field], before[field])
  assert.equal(saved.regional.standardsSnapshotId, before.regional.standardsSnapshotId)
  assert.ok(saved.workspaceRevision > invalid.workspaceRevision)
  await send('Page.reload'); await wait(`!!(${panel})`); await click('读取区域元数据')
  await wait(`!!(${control('us.supplementEffectiveDateType')})&&(${control('us.supplementEffectiveDateType')}).value==='fdasedt1'`)
  assert.deepEqual((await request(`${sequence}/publishing-metadata`)).regional, saved.regional)
  captured.saved = saved.regional
  passed.push('regional form save and browser reload preserve exact snapshot, identifiers, collections and every unedited field')
  const generated = await request('/api/backbone/generate', { applicationId: id, sequenceNumber: '0000' })
  const selected = await request('/api/package-validation-inputs', { applicationId: id, path: generated.packagePath, sequenceNumber: '0000' })
  const regionalBytes = await readBytes(`/api/package-validation-inputs/${selected.id}/file?logicalPath=0000%2Fm1%2Fus%2Fus-regional.xml`)
  const emitted = await evaluate(`(()=>{const xml=new DOMParser().parseFromString(${JSON.stringify(regionalBytes.toString('utf8'))},'application/xml');if(xml.querySelector('parsererror'))throw Error('Generated regional XML failed to parse');return{applicationType:xml.querySelector('application-number').getAttribute('application-type'),applicantIdentifier:xml.querySelector('applicant-info > id').textContent,submissionType:xml.querySelector('submission-id').getAttribute('submission-type'),effectiveType:xml.querySelector('submission-id').getAttribute('supplement-effective-date-type'),contactType:xml.querySelector('applicant-contact-name').getAttribute('applicant-contact-type'),phoneType:xml.querySelector('telephone').getAttribute('telephone-number-type')}})()`)
  assert.deepEqual(emitted, { applicationType: 'fdaat1', applicantIdentifier: '001234567', submissionType: 'fdast2',
    effectiveType: 'fdasedt1', contactType: 'fdaact1', phoneType: 'fdatnt1' })
  await writeFile(`${results}/regional-form-output.xml`, regionalBytes)
  let run = await request('/api/package-validations', { inputId: selected.id, profileSnapshotId: 'us-fda-3.2.2-m1-3.3-development-v1', mode: 'Development', idempotencyKey: randomUUID() })
  await until(async () => { run = await request(`/api/package-validations/${run.id}`); return !['Pending', 'Running', 'Queued'].includes(run.status) }, 'regional UI output independent validation', 400)
  assert.equal(run.status, 'Completed'); assert.equal(run.reportAvailable, true)
  const reportBytes = await readBytes(`/api/package-validations/${run.id}/report`), report = JSON.parse(reportBytes.toString('utf8'))
  const rules = ['FDA47-1496', 'FDA47-2013', 'US-SUPPLEMENT-EFFECTIVE-COMBINATION']
  for (const rule of rules) assert.equal(report.coverage.find(item => item.ruleId === rule)?.checkStatus, 'Pass', rule)
  assert.equal(report.isReadyForFinalization, false)
  await writeFile(`${results}/regional-form-validation.json`, reportBytes)
  captured.emitted = emitted; captured.run = run; captured.rules = rules
  passed.push('regional browser edits emit exact official XML values and independent dictionary/combination rules pass in Development')
}

const root = resolve('..'), results = resolve('../results'), backend = resolve('../backend')
const sdk = '/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423'
// Chromium's singleton socket must fit Linux's Unix-domain socket path limit.
// Keep this exclusively created profile on /data and retain its exact path in evidence.
const workspace = `${root}/browser-workspace`, profile = await mkdtemp('/data/home/ratools-browser-')
for (const path of [workspace, `${profile}/tmp`, `${profile}/downloads`]) await mkdir(path)
const apiKey = randomBytes(32).toString('hex') // Task-only auth, never persisted in evidence.
const postgres = process.env.QA07_POSTGRES === 'true'
const env = { ...process.env, DOTNET_ROOT: sdk, DOTNET_EnableDiagnostics: '0', DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://127.0.0.1:0',
  ASPNETCORE_HOSTINGSTARTUPASSEMBLIES: 'BrowserStartup', Persistence__Provider: postgres ? 'PostgreSql' : 'InMemory', Deployment__Mode: 'LocalOnly',
  Security__ApiKey: apiKey, Security__AllowedWorkspaceRoots__0: workspace,
  FileSecrets__ApiKeyPath: '', FileSecrets__PostgreSqlPasswordPath: '',
  FileStorage__RootPath: `${workspace}/uploads`, BackboneOutput__RootPath: `${workspace}/publish`, BackboneOutput__RetainJobRuns: '0',
  PackageValidation__StorageRoot: `${workspace}/validation`, TMPDIR: `${profile}/tmp` }
let apiLog = '', browserLog = '', browser, api, socket, failure, base, inspectPage
const errors = [], passed = [], facts = {}, downloadEvents = []
// Independent, deterministic one-page PDF fixture, with real xref offsets.
const content = 'BT /F1 12 Tf 72 720 Td (Synthetic QA07 evidence) Tj ET\n'
const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
  '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
  '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', `<< /Length ${content.length} >>\nstream\n${content}endstream`]
let pdfText = '%PDF-1.4\n'; const offsets = [0]
for (const [index, object] of objects.entries()) { offsets.push(Buffer.byteLength(pdfText)); pdfText += `${index + 1} 0 obj\n${object}\nendobj\n` }
const xref = Buffer.byteLength(pdfText)
pdfText += `xref\n0 6\n0000000000 65535 f \n${offsets.slice(1).map(value => `${String(value).padStart(10, '0')} 00000 n \n`).join('')}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`
const pdfBytes = Buffer.from(pdfText, 'ascii'), pdfSha256 = createHash('sha256').update(pdfBytes).digest('hex')
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const until = async (fn, label, attempts = 200) => { const deadline = Date.now() + attempts * 150; while (Date.now() < deadline) { if (await fn()) return; await delay(150) } throw Error(`Timed out: ${label}`) }
const stop = async process => {
  if (process?.pid && process.exitCode === null && process.signalCode === null) {
    process.kill('SIGTERM')
    await Promise.race([new Promise(resolve => process.once('exit', resolve)), delay(3000)])
    if (process.exitCode === null && process.signalCode === null) {
      process.kill('SIGKILL')
      await until(() => process.exitCode !== null || process.signalCode !== null, 'owned process stopped', 40)
    }
  }
}
const migrate = async label => {
  const child = startProcess(`${sdk}/dotnet`, [process.env.QA07_MIGRATOR_DLL || `${backend}/RATools.DatabaseMigrator.dll`], { env, cwd: backend, stdio: ['ignore', 'pipe', 'pipe'] })
  let log = ''; child.process.stdout.on('data', data => { log += data }); child.process.stderr.on('data', data => { log += data })
  try {
    await child.ready
    await until(() => child.process.exitCode !== null || child.process.signalCode !== null, 'product migrations', 400)
    assert.equal(child.process.exitCode, 0, 'See migration log')
    assert.equal(log.trim(), label === 'product-migrations-first'
      ? 'Applied 31 database migration(s); schema is current.'
      : 'Database schema is current; no migrations were applied.')
  }
  finally { await stop(child.process); await writeFile(`${results}/${label}.log`, log.replaceAll(apiKey, '[task-credential-redacted]')) }
}
const startApi = async (disableWorker = false) => {
  const child = startProcess(`${sdk}/dotnet`, [`${backend}/RATools.Api.dll`, '--contentRoot', backend, '--webroot', resolve('dist')],
    { env: { ...env, PackageValidation__Enabled: disableWorker ? 'false' : 'true', ...(base ? { ASPNETCORE_URLS: base } : {}) }, cwd: backend, stdio: ['ignore', 'pipe', 'pipe'] })
  api = child.process
  api.stdout.on('data', data => { apiLog += data }); api.stderr.on('data', data => { apiLog += data })
  await child.ready
  await until(async () => {
    if (api.exitCode !== null) throw Error(`API startup exited ${api.exitCode}; see backend-api.log`)
    base = apiLog.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1]
    return base && (await fetch(base + '/health').catch(() => null))?.ok
  }, 'API ready')
}
const request = async (path, body, expected, method = 'POST') => {
  const response = await fetch(base + path, { headers: { 'X-RA-Tools-Api-Key': apiKey, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    ...(body ? { method, body: JSON.stringify(body) } : {}) })
  const text = await response.text()
  if (expected !== undefined) assert.equal(response.status, expected, `${path}: ${text}`)
  else assert.ok(response.ok, `${response.status} ${path}: ${text}`)
  return text ? JSON.parse(text) : undefined
}
try {
  if (postgres) await migrate('product-migrations-first')
  await startApi()
  facts.application = await request('/api/applications', { applicationNumber: '987654', sponsorName: 'Synthetic QA07 browser fixture',
    ectdTemplateKey: 'us-fda-ectd-3.2.2', workingDirectoryParentPath: workspace })
  const id = facts.application.id, seq = `/api/applications/${id}/sequences/0000`
  await request(`/api/applications/${id}/sequences`, { sequenceNumber: '0000', submissionType: 'original-application', description: 'Synthetic nonempty Development capture' })
  facts.before = await request(`${seq}/workspace`)
  facts.missingMetadata = await request('/api/backbone/generate', { applicationId: id, sequenceNumber: '0000' }, 400)
  assert.equal(facts.missingMetadata.code, 'US_REGIONAL_METADATA_MISSING')
  assert.equal(facts.missingMetadata.fieldPath, 'us.applicantIdentifier')
  passed.push('missing US metadata reports a client error with its exact field')
  const schema = await request(`/api/applications/${id}/regional-metadata/schema`)
  assert.equal(schema.regionKey, 'US'); assert.ok(schema.standardsSnapshotId)
  const regional = { standardsSnapshotId: schema.standardsSnapshotId, content: { schemaVersion: 1, regionKey: 'US', eu: null,
    us: { applicationIdentifier: '987654', applicationType: 'fdaat3', applicantName: 'Synthetic QA07',
      applicantIdentifier: '001234567', submissionIdentifier: '0000', submissionType: 'fdast1', submissionSubtype: 'fdasst3',
      formType: null, description: 'Synthetic Development evidence',
      contacts: [{ id: randomUUID(), name: 'Synthetic contact', type: 'fdaact1',
        phones: [{ id: randomUUID(), number: '0000000000', type: 'fdatnt1' }],
        emails: [{ id: randomUUID(), value: 'qa07@example.invalid' }] }] } } }
  facts.metadata = await request(`${seq}/publishing-metadata`, { expectedRevision: facts.before.workspaceRevision, applicationType: 'ind',
    submissionType: 'original-application', submissionSubtype: 'application', sequenceDescription: 'Synthetic Development evidence',
    applicantName: 'Synthetic QA07', applicantContactName: 'Synthetic contact', applicantContactType: 'regulatory',
    telephone: '0000000000', telephoneNumberType: 'business', email: 'qa07@example.invalid', regional }, undefined, 'PUT')
  assert.equal(facts.metadata.regional.standardsSnapshotId, schema.standardsSnapshotId)
  assert.deepEqual(facts.metadata.regional.content, regional.content)
  assert.deepEqual((await request(`${seq}/publishing-metadata`)).regional, facts.metadata.regional)
  passed.push('typed regional metadata preserves explicit identifiers, archived codes and exact snapshot across save/read')
  let tree = await request(`${seq}/nodes`)
  tree = await request(`${seq}/nodes`, { expectedRevision: tree.workspaceRevision, definitionKey: 'm2-common-technical-document-summaries', attributes: {} })
  const parent = tree.nodes.find(node => node.definitionKey === 'm2-common-technical-document-summaries')
  tree = await request(`${seq}/nodes`, { expectedRevision: tree.workspaceRevision, definitionKey: 'm2-2-introduction', parentInstanceId: parent.nodeInstanceId, attributes: {} })
  const node = tree.nodes.find(node => node.definitionKey === 'm2-2-introduction')
  const form = new FormData()
  form.append('expectedRevision', String(tree.workspaceRevision)); form.append('file', new Blob([pdfBytes], { type: 'application/pdf' }), 'qa07-synthetic.pdf')
  const uploaded = await fetch(base + `${seq}/assembly-sources`, { method: 'POST', headers: { 'X-RA-Tools-Api-Key': apiKey }, body: form })
  const uploadText = await uploaded.text(); assert.equal(uploaded.status, 200, uploadText)
  const source = JSON.parse(uploadText); assert.equal(source.sha256, pdfSha256)
  let plan = await request(`${seq}/assembly-plans`, { expectedRevision: tree.workspaceRevision,
    manifest: `sourceFile,nodeInstanceId,fileName,operation,title\n${source.sourceFile},${node.nodeInstanceId},qa07-synthetic.pdf,new,Synthetic QA07 evidence\n` })
  assert.equal(plan.plan.invalidRows, 0, JSON.stringify(plan.plan.diagnostics)); assert.equal(plan.plan.validRows, 1)
  plan = await request(`${seq}/assembly-plans/${plan.id}/selection`, { expectedRevision: tree.workspaceRevision, parentDigest: plan.plan.planDigest, selectedRows: [plan.plan.rows[0].lineNumber] })
  assert.equal(plan.canExecute, true, JSON.stringify(plan.plan.diagnostics))
  let assembly = await request(`${seq}/assembly-executions`, { planId: plan.id, planDigest: plan.plan.planDigest, idempotencyKey: randomUUID() }, 202)
  await until(async () => { assembly = await request(`${seq}/assembly-executions/${assembly.id}`); return ![0, 1].includes(assembly.status) }, 'PDF assembly completed', 400)
  assert.equal(assembly.status, 2, JSON.stringify(assembly)); assert.equal(assembly.result.documents.length, 1)
  facts.pdf = { sha256: pdfSha256, length: pdfBytes.length, source, assembly, targetHref: assembly.result.documents[0].href }
  await writeFile(`${results}/synthetic.pdf`, pdfBytes)
  passed.push('real assembly uploads, independently inspects and commits one synthetic PDF')
  facts.generated = await request('/api/backbone/generate', { applicationId: id, sequenceNumber: '0000' })
  const input = await request('/api/package-validation-inputs', { applicationId: id, path: facts.generated.packagePath, sequenceNumber: '0000' })
  facts.input = input
  let run = await request('/api/package-validations', { inputId: input.id, profileSnapshotId: 'us-fda-3.2.2-m1-3.3-development-v1', mode: 'Development', idempotencyKey: randomUUID() })
  await until(async () => { run = await request(`/api/package-validations/${run.id}`); return !['Pending', 'Running', 'Queued'].includes(run.status) }, 'independent validation completed', 400)
  facts.run = run
  assert.equal(run.status, 'Completed'); assert.equal(run.reportAvailable, true)
  const reportResponse = await fetch(base + `/api/package-validations/${run.id}/report`, { headers: { 'X-RA-Tools-Api-Key': apiKey } })
  assert.equal(reportResponse.status, 200)
  const reportBytes = Buffer.from(await reportResponse.arrayBuffer())
  assert.ok(reportBytes.length > 0); JSON.parse(reportBytes.toString('utf8'))
  facts.report = { length: reportBytes.length, sha256: createHash('sha256').update(reportBytes).digest('hex') }
  await writeFile(`${results}/validation-report-before.json`, reportBytes)
  passed.push('real writer ZIP captured and independently validated')
  const browserStart = startProcess('/usr/lib/chromium/chromium', ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage', '--no-first-run',
    '--no-default-browser-check', '--disable-breakpad', '--disable-crash-reporter', `--user-data-dir=${profile}`,
    '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0', 'about:blank'],
    { env: { ...process.env, TMPDIR: `${profile}/tmp`, XDG_CONFIG_HOME: profile, XDG_CACHE_HOME: profile, XDG_DATA_HOME: profile }, stdio: ['ignore', 'pipe', 'pipe'] })
  browser = browserStart.process
  browser.stdout.on('data', data => { browserLog += data }); browser.stderr.on('data', data => { browserLog += data })
  await browserStart.ready
  let port
  await until(async () => { try { port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0]; return true } catch { return false } }, 'Chromium ready')
  const target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(item => item.type === 'page')
  socket = new WebSocket(target.webSocketDebuggerUrl)
  await new Promise((resolve, reject) => { const timeout = setTimeout(() => reject(Error('CDP connection timeout')), 10000); socket.onopen = () => { clearTimeout(timeout); resolve() }; socket.onerror = error => { clearTimeout(timeout); reject(error) } })
  let counter = 0; const pending = new Map()
  socket.onmessage = event => {
    const value = JSON.parse(event.data)
    if (value.id) { const action = pending.get(value.id); if (!action) return; pending.delete(value.id); value.error ? action.reject(Error(JSON.stringify(value.error))) : action.resolve(value.result) }
    else if (value.method === 'Runtime.exceptionThrown') errors.push(value.params.exceptionDetails.exception?.description ?? value.params.exceptionDetails.text)
    else if (value.method.startsWith('Browser.download')) downloadEvents.push({ method: value.method, ...value.params })
  }
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++counter; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params }))
    setTimeout(() => { if (pending.delete(id)) reject(Error(`CDP timeout: ${method}`)) }, 20000).unref()
  })
  const evaluate = async expression => { const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); if (result.exceptionDetails) throw Error(result.exceptionDetails.exception?.description); return result.result.value }
  inspectPage = () => evaluate('document.body.innerText')
  const wait = expression => until(() => evaluate(expression), expression)
  const click = async label => {
    await wait(`[...document.querySelectorAll('button')].some(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled)`)
    await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).click()`)
  }
  await send('Runtime.enable'); await send('Page.enable')
  await send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: `${profile}/downloads`, eventsEnabled: true })
  await send('Page.navigate', { url: `${base}/applications/${id}/sequences/0000/workspace` })
  await click('定稿与交付记录'); await click('选择验包历史'); await click('使用此运行')
  await wait(`!!document.querySelector('[name="confirmFinalization"]')`)
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent==='保存开发快照').disabled`), true)
  await evaluate(`document.querySelector('[name="confirmFinalization"]').click()`)
  await click('保存开发快照'); await wait(`document.body.innerText.includes('快照已保存')`)
  const versions = await request(`${seq}/finalized-versions`)
  assert.equal(versions.length, 1)
  const snapshot = versions[0]; facts.snapshot = snapshot
  assert.equal(snapshot.validationRunId, run.id); assert.equal(snapshot.validationMode, 'Development'); assert.equal(snapshot.isReadyForFinalization, false)
  facts.locked = await request(`${seq}/workspace`)
  assert.equal(facts.locked.finalization.isEditable, false)
  const manifest = await request(`/api/finalizations/${snapshot.id}/manifest`); facts.manifest = manifest
  assert.equal(manifest.digest, snapshot.manifestDigest)
  const archivedPdf = manifest.files.filter(file => file.relativePath.endsWith('/qa07-synthetic.pdf'))
  assert.equal(archivedPdf.length, 1); assert.equal(archivedPdf[0].sha256, pdfSha256); assert.equal(archivedPdf[0].length, pdfBytes.length)
  passed.push('explicit UI capture binds the actual run and locks the workspace')
  await wait(`document.body.innerText.includes('开发快照不能登记交付')`)
  assert.equal(await evaluate(`!!document.querySelector('[data-testid="delivery-form"]')`), false)
  facts.deliveryRejection = await request(`/api/finalized-sequences/${snapshot.id}/deliveries`,
    { packageSha256: manifest.files.find(file => file.relativePath === 'delivery-package.zip').sha256, channel: 'synthetic', recordedBy: 'QA07' }, 409)
  passed.push('Development delivery rejected by both UI and actual backend')
  await click('下载交付包')
  await until(async () => { try { facts.downloadLength = (await readFile(`${profile}/downloads/delivery-package.zip`)).length; return facts.downloadLength > 0 } catch { return false } }, 'downloaded ZIP')
  await until(() => downloadEvents.some(event => event.state === 'completed'), 'download completed')
  const bytes = await readFile(`${profile}/downloads/delivery-package.zip`)
  const zip = manifest.files.find(file => file.relativePath === 'delivery-package.zip')
  assert.equal(bytes.length, zip.length)
  const digest = createHash('sha256').update(bytes).digest('hex'); assert.equal(digest, zip.sha256)
  facts.download = { length: bytes.length, sha256: digest, retainedPath: `${profile}/downloads/delivery-package.zip` }
  passed.push('actual browser ZIP download matches immutable manifest length and SHA256')
  await click('创建新草稿修订'); await wait(`document.body.innerText.includes('新草稿修订已创建')`)
  await wait(`!!document.querySelector('[name="validationRunId"]')`)
  facts.draft = await request(`${seq}/workspace`)
  assert.equal(facts.draft.finalization.isEditable, true)
  assert.ok(facts.draft.workspaceRevision > facts.locked.workspaceRevision)
  assert.deepEqual(await request(`/api/finalizations/${snapshot.id}/manifest`), manifest)
  await send('Page.reload')
  await click('定稿与交付记录'); await click('查看快照')
  await wait(`document.body.innerText.includes(${JSON.stringify(snapshot.id)})`)
  assert.equal((await request(`${seq}/finalized-versions`)).length, 1)
  passed.push('new draft reopens editing; old snapshot survives browser reload unchanged')
  if (postgres) {
    const previousPid = api.pid
    await stop(api)
    assert.equal(api.exitCode, 0, 'API must shut down cleanly before database restart')
    await writeFile(`${results}/postgres-restart-request`, 'API stopped')
    await until(async () => { try { return (await readFile(`${results}/postgres-restart-ready`, 'utf8')) === 'database restarted' } catch { return false } }, 'database restart', 400)
    await migrate('product-migrations-repeat')
    await startApi()
    assert.notEqual(api.pid, previousPid)
    assert.deepEqual(await request(`/api/finalizations/${snapshot.id}/manifest`), manifest)
    assert.deepEqual(await request(`${seq}/workspace`), facts.draft)
    assert.equal((await request(`/api/package-validations/${run.id}`)).reportAvailable, true)
    const restoredReport = await fetch(base + `/api/package-validations/${run.id}/report`, { headers: { 'X-RA-Tools-Api-Key': apiKey } })
    assert.equal(restoredReport.status, 200)
    const restoredBytes = Buffer.from(await restoredReport.arrayBuffer())
    assert.deepEqual(restoredBytes, reportBytes, 'Archived report bytes changed or were lost across restart')
    await writeFile(`${results}/validation-report-after.json`, restoredBytes)
    assert.deepEqual(await request(`${seq}/finalized-versions`), versions)
    await send('Page.reload'); await click('定稿与交付记录'); await click('查看快照')
    await wait(`document.body.innerText.includes(${JSON.stringify(snapshot.id)})`)
    facts.restart = { previousApiPid: previousPid, apiPid: api.pid, databaseRestarted: true, manifestUnchanged: true, workspaceUnchanged: true }
    passed.push('actual API and PostgreSQL stop/restart retain migrated workspace, validation and immutable snapshot in browser')
  }
  const readBytes = async path => {
    const response = await fetch(base + path, { headers: { 'X-RA-Tools-Api-Key': apiKey } })
    assert.equal(response.status, 200, path)
    return Buffer.from(await response.arrayBuffer())
  }
  await exerciseRegionalForm({ id, base, send, evaluate, request, until, results, passed, facts, readBytes })
  assert.deepEqual(await request(`/api/finalizations/${snapshot.id}/manifest`), manifest)
  await exerciseExternalInputs({ root, results, workspace, profile, base, send, evaluate, request, until, stop,
    requestBytes: readBytes, stopApi: () => stop(api), startApi, downloadEvents, passed, facts })
  assert.deepEqual(errors, [])
} catch (error) {
  failure = error; errors.push(String(error))
  if (inspectPage) await writeFile(`${results}/backend-browser-page.txt`, await inspectPage()).catch(() => {})
} finally {
  socket?.close()
  for (const process of [browser, api].filter(Boolean)) await stop(process)
  await writeFile(`${results}/backend-api.log`, apiLog.replaceAll(apiKey, '[task-credential-redacted]'))
  await writeFile(`${results}/backend-chromium.log`, browserLog)
  await writeFile(`${results}/backend-browser.json`, JSON.stringify({ scope: `Current production API/${postgres ? 'PostgreSql with process restart' : 'InMemory'} and frontend; synthetic one-page PDF US Development sequence`,
    passed, errors, facts, downloadEvents, retainedProfile: profile, unrun: ['Formal delivery/receipt chain', ...(!postgres ? ['PostgreSQL-backed browser restart'] : []), 'complex PDF content qualification'] }, null, 2))
}
if (failure) throw failure
console.log(`PASS real-backend browser checks: ${passed.length}`)
