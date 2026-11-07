// Real product API and production UI; synthetic one-page PDF, Development only.
import assert from 'node:assert/strict'
import { randomBytes, randomUUID, createHash } from 'node:crypto'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { startProcess, fetchDeadline as fetch } from './browser-process.mjs'

const root = resolve('..'), results = resolve('../results'), backend = resolve('../backend')
const sdk = '/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423'
const workspace = `${root}/browser-workspace`, profile = `/data/home/qa07-real-${randomUUID().slice(0, 8)}`
for (const path of [workspace, profile, `${profile}/tmp`, `${profile}/downloads`]) await mkdir(path)
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
  const child = startProcess(`${sdk}/dotnet`, [`${backend}/RATools.DatabaseMigrator.dll`], { env, cwd: backend, stdio: ['ignore', 'pipe', 'pipe'] })
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
const startApi = async () => {
  const child = startProcess(`${sdk}/dotnet`, [`${backend}/RATools.Api.dll`, '--contentRoot', backend, '--webroot', resolve('dist')],
    { env: { ...env, ...(base ? { ASPNETCORE_URLS: base } : {}) }, cwd: backend, stdio: ['ignore', 'pipe', 'pipe'] })
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
  assert.equal(facts.missingMetadata.fieldPath, 'ApplicantContactName')
  passed.push('missing US metadata reports a client error with its exact field')
  await request(`${seq}/publishing-metadata`, { expectedRevision: facts.before.workspaceRevision, applicationType: 'ind',
    submissionType: 'original-application', submissionSubtype: 'application', sequenceDescription: 'Synthetic Development evidence',
    applicantName: 'Synthetic QA07', applicantContactName: 'Synthetic contact', applicantContactType: 'regulatory',
    telephone: '0000000000', telephoneNumberType: 'business', email: 'qa07@example.invalid' }, undefined, 'PUT')
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
