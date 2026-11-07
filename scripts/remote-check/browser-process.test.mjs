import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { startProcess, fetchDeadline } from './browser-process.mjs'

if (process.argv[2] === 'spawn') {
  const child = startProcess('/missing-qa07-executable', [], { stdio: 'ignore' })
  await assert.rejects(child.ready, /ENOENT/)
  assert.equal(child.process.pid, undefined)
  console.log('PASS failed spawn is caught before cleanup')
} else {
  const server = createServer(() => {})
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  try {
    const result = await Promise.race([
      fetchDeadline(`http://127.0.0.1:${server.address().port}`, {}, 100).then(() => 'response', () => 'aborted'),
      new Promise(resolve => setTimeout(() => resolve('no deadline'), 1000)),
    ])
    assert.equal(result, 'aborted')
    console.log('PASS nonresponsive HTTP request deadline')
  } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)) }
}
