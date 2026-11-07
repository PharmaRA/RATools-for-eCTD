// Production UI in real Chromium, synthetic API contracts. This does not qualify
// the backend or any regional profile for Formal capture/delivery.
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { resolve, extname } from 'node:path'
import { spawn } from 'node:child_process'
import { randomUUID } from 'node:crypto'

const results = resolve('../results')
const profile = `/data/home/qa07-browser-${randomUUID().slice(0, 8)}`
await mkdir(profile)
await mkdir(`${profile}/tmp`)
const passed = [], errors = [], requests = [], attempts = [], downloads = []
let revision = 7, editable = true, versions = [], deliveries = [], receipts = [], snapshotFailure = false
const binding = { inputManifestDigest: 'a'.repeat(64), historyBaselineDigest: 'b'.repeat(64), profileSnapshotId: 'profile',
  rulesDigest: 'c'.repeat(64), resourceLimitsDigest: 'd'.repeat(64), engineVersion: 'synthetic-browser-fixture',
  validationMode: 'Development', standardsSnapshotId: 'standard' }
const run = { id: 'run-1', applicationId: 'app', sequenceNumber: '0000', status: 'Completed', reportAvailable: true,
  binding, createdUtc: '2026-11-07T00:00:00Z' }
const report = { binding, runStatus: 'Completed', executionCompleted: true, isReadyForFinalization: false,
  blockingReasons: ['SyntheticUnqualifiedProfile'] }
const snapshot = { id: 'snapshot', applicationId: 'app', sequenceNumber: '0000', sourceWorkspaceRevision: 7,
  manifestDigest: 'e'.repeat(64), validationMode: 'Development', isReadyForFinalization: false,
  standardsProfileSnapshotId: 'profile', ichEctdVersion: '3.2.2', regionalModule1Version: '3.1.1',
  historyBaselineDigest: binding.historyBaselineDigest, validationRunId: run.id, blockingReasons: report.blockingReasons,
  createdUtc: '2026-11-07T00:00:00Z' }
const manifest = { digest: snapshot.manifestDigest, applicationId: 'app', sequenceNumber: '0000', sourceWorkspaceRevision: 7,
  files: [{ relativePath: 'delivery-package.zip', kind: 3, sha256: 'f'.repeat(64), length: 17, md5: '0'.repeat(32) }],
  nodes: [], leaves: [], lifecycle: [], validation: {} }
const server = createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost'), path = url.pathname
    const chunks = []; for await (const chunk of req) chunks.push(chunk)
    const body = chunks.length ? JSON.parse(Buffer.concat(chunks)) : undefined
    requests.push({ method: req.method, path: req.url, body })
    const json = (value, status = 200) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(value)) }
    if (path === '/runtime-config') return json({})
    if (path === '/health') return json({ status: 'ok' })
    if (path.startsWith('/api/') && path.endsWith('/workspace')) {
      if (snapshotFailure) return json({ message: '合成工作区读取失败' }, 503)
      return json({ workspaceRevision: revision, placements: [], documents: [],
        finalization: versions.length ? { finalizedSequenceId: snapshot.id, sourceWorkspaceRevision: 7, isEditable: editable, draftWorkspaceRevision: editable ? revision : null } : null })
    }
    if (path.endsWith('/ectd-structure')) return json({ roots: [] })
    if (path.endsWith('/finalized-versions')) return json(versions)
    if (path === '/api/package-validations') return json({ items: [run], totalCount: 1, offset: 0, limit: 20 })
    if (path === '/api/package-validations/run-1') return json(run)
    if (path === '/api/package-validations/run-1/report') return json(report)
    if (path === '/api/standards-snapshots/standard') return json({ id: 'standard', ruleProfileAlias: 'profile', ichEctdVersion: '3.2.2', regionalModule1Version: '3.1.1' })
    if (path.endsWith('/finalizations') && req.method === 'POST') {
      attempts.push(body)
      if (attempts.length === 1) return json({ message: '合成响应丢失' }, 503)
      assert.deepEqual(body, attempts[0]); versions = [snapshot]; editable = false
      return json({ sequence: snapshot, created: false })
    }
    if (path === '/api/finalizations/snapshot/manifest') return json(manifest)
    if (path === '/api/finalizations/snapshot/files') {
      downloads.push(url.searchParams.get('path'))
      res.writeHead(200, { 'content-type': 'application/octet-stream', 'content-disposition': 'attachment; filename="synthetic-evidence.bin"' })
      return res.end('synthetic fixture')
    }
    if (path.endsWith('/draft-revisions')) { revision++; editable = true; snapshotFailure = true; return json({ created: true, draft: { workspaceRevision: revision } }) }
    if (path.endsWith('/deliveries')) {
      if (req.method === 'POST') { assert.equal(body.packageSha256, 'f'.repeat(64)); const record = { id: 'delivery', ...body }; deliveries.push(record); return json(record) }
      return json(deliveries)
    }
    if (path.endsWith('/receipts')) {
      if (req.method === 'POST') { const record = { id: 'receipt', ...body, rawTextSha256: '1'.repeat(64) }; receipts.push(record); return json(record) }
      return json(receipts)
    }
    if (path === '/api/audit-logs') return json({ page: 1, pageSize: 20, totalCount: 0, items: [] })
    if (path.startsWith('/api/')) return json({ message: 'Synthetic fixture has no data for this optional panel.' }, 404)
    const staticPath = path.startsWith('/assets/') ? resolve('dist', '.' + path) : resolve('dist/index.html')
    assert.ok(staticPath.startsWith(resolve('dist') + '/'))
    const bytes = await readFile(staticPath)
    res.writeHead(200, { 'content-type': ({ '.js': 'application/javascript', '.css': 'text/css' })[extname(staticPath)] ?? 'text/html' })
    res.end(bytes)
  } catch (error) { errors.push(String(error)); res.writeHead(500); res.end('Synthetic handler failure') }
})
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
const base = `http://127.0.0.1:${server.address().port}`
const browser = spawn('/usr/lib/chromium/chromium', ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage',
  '--no-first-run', '--no-default-browser-check', '--disable-breakpad', '--disable-crash-reporter',
  `--user-data-dir=${profile}`, '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0', 'about:blank'],
{ env: { ...process.env, TMPDIR: `${profile}/tmp`, XDG_CONFIG_HOME: profile, XDG_CACHE_HOME: profile, XDG_DATA_HOME: profile }, stdio: ['ignore', 'pipe', 'pipe'] })
let browserLog = '', socket, failure, inspectPage
browser.stdout.on('data', chunk => { browserLog += chunk })
browser.stderr.on('data', chunk => { browserLog += chunk })
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const until = async fn => { for (let i = 0; i < 120; i++) { if (await fn()) return; await delay(100) } throw Error('Browser condition timed out') }
try {
  let port
  await until(async () => { try { port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0]; return true } catch { return false } })
  const target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(item => item.type === 'page')
  socket = new WebSocket(target.webSocketDebuggerUrl)
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject })
  let counter = 0; const pending = new Map()
  socket.onmessage = event => {
    const value = JSON.parse(event.data)
    if (value.id) { const action = pending.get(value.id); if (!action) return; pending.delete(value.id); value.error ? action.reject(Error(JSON.stringify(value.error))) : action.resolve(value.result) }
    else if (value.method === 'Runtime.exceptionThrown') errors.push(value.params.exceptionDetails.exception?.description ?? value.params.exceptionDetails.text)
    else if (value.method === 'Network.loadingFailed') browserLog += `\nNetwork: ${value.params.errorText}`
  }
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++counter; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params }))
    setTimeout(() => { if (pending.delete(id)) reject(Error(`CDP timeout: ${method}`)) }, 20000).unref()
  })
  const evaluate = async expression => { const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); if (result.exceptionDetails) throw Error(result.exceptionDetails.exception?.description); return result.result.value }
  inspectPage = () => evaluate('JSON.stringify({url:location.href,text:document.body.innerText,html:document.documentElement.outerHTML.slice(0,2000)})')
  const wait = expression => until(() => evaluate(expression))
  const click = async label => {
    await wait(`[...document.querySelectorAll('button')].some(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled)`)
    await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).click()`)
  }
  const fill = (name, value) => evaluate(`(()=>{const e=document.querySelector('[name="${name}"]');const p=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:e.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(p,'value').set.call(e,${JSON.stringify(value)});e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()`)
  await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable')
  await send('Page.setDownloadBehavior', { behavior: 'deny' })
  const navigation = await send('Page.navigate', { url: `${base}/applications/app/sequences/0000/workspace` })
  if (navigation.errorText) throw Error(navigation.errorText)
  await click('定稿与交付记录'); await click('选择验包历史'); await click('使用此运行')
  await wait(`document.body.innerText.includes('SyntheticUnqualifiedProfile')`)
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent==='保存开发快照').disabled`), true)
  await evaluate(`document.querySelector('[name="confirmFinalization"]').click()`)
  await click('保存开发快照'); await wait(`document.body.innerText.includes('合成响应丢失')`)
  await click('定稿与交付记录'); await click('定稿与交付记录')
  await fill('validationRunId', 'run-1'); await click('读取验证证据')
  await wait(`!!document.querySelector('[name="confirmFinalization"]')`)
  await evaluate(`document.querySelector('[name="confirmFinalization"]').click()`)
  await click('保存开发快照'); await wait(`document.body.innerText.includes('快照已保存')`)
  assert.equal(attempts.length, 2); assert.deepEqual(attempts[0], attempts[1])
  passed.push('explicit development confirmation and same request after panel reopen')
  await wait(`document.body.innerText.includes('开发快照不能登记交付')`)
  assert.equal(await evaluate(`!!document.querySelector('[data-testid="delivery-form"]')`), false)
  await click('下载交付包'); await until(() => downloads.includes('delivery-package.zip'))
  passed.push('development delivery blocked; immutable ZIP endpoint requested (download persistence not asserted)')
  await click('创建新草稿修订'); await wait(`document.body.innerText.includes('新草稿修订已创建')`)
  await wait(`document.body.innerText.includes('重试刷新工作区')`)
  snapshotFailure = false
  await click('重试刷新工作区')
  await wait(`!!document.querySelector('[name="validationRunId"]')`)
  assert.equal(requests.filter(req => req.path.endsWith('/draft-revisions')).length, 1)
  passed.push('new draft reopens workspace while old snapshot remains')
  // Explicit Formal fixture exercises UI wiring only; no backend qualification.
  versions = [{ ...snapshot, validationMode: 'Formal', isReadyForFinalization: true }]
  await click('刷新快照'); await delay(250); await click('查看快照')
  await wait(`!!document.querySelector('[data-testid="delivery-form"]')`)
  await fill('channel', 'synthetic portal'); await fill('recordedBy', 'synthetic operator')
  await click('追加交付记录'); await click('查看回执')
  await fill('kind', '0'); await fill('outcome', '传输完成'); await fill('rawText', 'synthetic original receipt'); await fill('receiptRecordedBy', 'synthetic operator')
  await click('追加回执'); await until(() => receipts.length === 1)
  assert.equal(receipts[0].kind, 0); assert.equal(receipts[0].rawText, 'synthetic original receipt')
  assert.equal(deliveries[0].packageSha256, 'f'.repeat(64))
  passed.push('synthetic Formal fixture binds ZIP hash and retains transmission receipt category')
  await click('审计日志'); await wait(`!!document.querySelector('#createdFromLocal')`)
  await evaluate(`(()=>{for(const [id,v] of [['createdFromLocal','2026-11-07T08:30:15'],['createdToLocal','2026-11-08T09:45:30']]){const e=document.getElementById(id);Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));}})()`)
  await click('查询'); await until(() => requests.some(req => req.path.includes('createdFromUtc=')))
  await click('重置'); await wait(`document.getElementById('createdFromLocal').value===''&&document.getElementById('createdToLocal').value===''`)
  passed.push('native date-time filtering and reset in Chromium')
  assert.deepEqual(errors, [])
  await writeFile(resolve(results, 'browser-page.txt'), await evaluate('document.body.innerText'))
} catch (error) { failure = error; errors.push(String(error)); if (inspectPage) await writeFile(resolve(results, 'browser-failure-page.json'), await inspectPage()).catch(() => {}) }
finally {
  socket?.close()
  browser.kill('SIGTERM')
  await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(3000)])
  if (browser.exitCode === null && browser.signalCode === null) { browser.kill('SIGKILL'); await new Promise(resolve => browser.once('exit', resolve)) }
  await new Promise(resolve => server.close(resolve))
  await writeFile(resolve(results, 'browser-process.log'), browserLog)
  await writeFile(resolve(results, 'browser-contract.json'), JSON.stringify({ scope: 'production frontend with synthetic API contracts; no real backend or Formal qualification', passed, errors, attempts, downloads, deliveries, receipts, requests, retainedProfile: profile }, null, 2))
}
if (failure) throw failure
console.log(`PASS Chromium contract workflows: ${passed.length}; synthetic API only`)
