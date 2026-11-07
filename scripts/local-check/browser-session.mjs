import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { readFile, writeFile } from 'node:fs/promises'
import { resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../../', import.meta.url))
export const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
export async function until(check, label, timeout = 30000) {
  const deadline = Date.now() + timeout
  while (Date.now() < deadline) { if (await check()) return; await delay(50) }
  throw Error('Timeout: ' + label)
}
export async function browserSession(evidence) {
  evidence = resolve(evidence)
  assert.ok(evidence.startsWith(resolve(root, '.artifacts/local-check') + sep))
  const data = resolve(evidence, 'browser-data')
  const guard = JSON.parse(await readFile(resolve(evidence, 'browser-guard.json'), 'utf8'))
  assert.equal(guard.deleteAccessDenied, true)
  assert.equal(resolve(guard.guardRoot), data)
  const executable = resolve(root, '.artifacts/tools/chrome-headless-shell-157.0.8103.38/chrome-headless-shell-win64/chrome-headless-shell.exe')
  let log = '', socket, exited = false, counter = 0
  const pending = new Map(), events = []
  const child = spawn(executable, ['--no-sandbox', '--disable-gpu', '--disable-breakpad', '--disable-crash-reporter',
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0', `--user-data-dir=${resolve(data,'profile')}`,
    `--disk-cache-dir=${resolve(data,'cache')}`, 'about:blank'], { cwd: data, windowsHide: true,
    env: { ...process.env, TEMP: resolve(data,'temp'), TMP: resolve(data,'temp'), APPDATA: resolve(data,'appdata'),
      LOCALAPPDATA: resolve(data,'localappdata'), USERPROFILE: data }, stdio: ['ignore','pipe','pipe'] })
  child.on('exit', () => { exited = true })
  child.stdout.on('data', bytes => { log += bytes }); child.stderr.on('data', bytes => { log += bytes })
  const stop = async () => {
    try {
      if (socket?.readyState === WebSocket.OPEN) { try { await send('Browser.close') } catch { /* connection closes with the browser */ } }
      socket?.close()
      if (!exited) { await Promise.race([until(() => exited, 'browser exit', 5000), delay(5000)]); }
      if (!exited) { child.kill(); await until(() => exited, 'browser forced exit', 5000) }
    } finally { await writeFile(resolve(evidence,'browser.log'),log) }
  }
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++counter
    const timer = setTimeout(() => { pending.delete(id); reject(Error('CDP timeout: '+method)) },10000)
    pending.set(id,{ resolve: value => { clearTimeout(timer); resolve(value) }, reject: error => { clearTimeout(timer); reject(error) } })
    socket.send(JSON.stringify({ id, method, params }))
  })
  try {
    await new Promise((resolve,reject) => { child.once('spawn',resolve); child.once('error',reject) })
    await until(() => { if (exited) throw Error('Browser exited during startup'); return /DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)/.test(log) },'browser CDP endpoint')
    const port = /DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)/.exec(log)[1]
    const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`,{signal:AbortSignal.timeout(5000)})).json()
    const page = pages.find(item=>item.type==='page'); assert.ok(page)
    socket = new WebSocket(page.webSocketDebuggerUrl)
    await new Promise((resolve,reject) => { const timeout=setTimeout(()=>reject(Error('CDP connection timeout')),5000); socket.onopen=()=>{clearTimeout(timeout);resolve()};socket.onerror=error=>{clearTimeout(timeout);reject(error)} })
    socket.onmessage = event => {
      const message = JSON.parse(event.data)
      if (message.id) { const promise=pending.get(message.id);if(!promise)return;pending.delete(message.id);message.error?promise.reject(Error(JSON.stringify(message.error))):promise.resolve(message.result) }
      else events.push(message)
    }
    const evaluate = async expression => {
      const result=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true})
      if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text)
      return result.result.value
    }
    await send('Runtime.enable');await send('Page.enable')
    // Downloads are observed through the page's Blob/response, not native file cleanup.
    await send('Browser.setDownloadBehavior',{behavior:'deny'})
    return {send,evaluate,events,stop,isStopped:()=>exited}
  } catch(error) { await stop();throw error }
}
