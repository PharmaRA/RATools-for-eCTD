import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import { resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { startVitest } from '../../frontend/node_modules/vitest/dist/node.js'
import react from '../../frontend/node_modules/@vitejs/plugin-react/dist/index.js'

const root = fileURLToPath(new URL('../../', import.meta.url))
const frontend = resolve(root, 'frontend')
const evidence = resolve(process.argv[2])
assert.ok(evidence.startsWith(resolve(root, '.artifacts/local-check') + sep))
await mkdir(evidence, { recursive: false })
const files = ['src/pages/workspace/useWorkspaceDragDrop.test.tsx', 'src/pages/SequenceWorkspacePage.validation.test.tsx']
const inputs = ['src/pages/workspace/useWorkspaceDragDrop.ts', 'src/pages/SequenceWorkspacePage.tsx', 'src/pages/LeafMetadataPanel.tsx', ...files]
const hashes = {}
for (const path of inputs) hashes[path] = createHash('sha256').update(await readFile(resolve(frontend, path))).digest('hex')
let context
const summary = { exitCode: 1, hashes, scope: 'Windows jsdom scoped workspace regression; not browser/API qualification' }
try {
  process.chdir(frontend)
  const guard = new URL('../remote-check/no-delete.mjs', import.meta.url).href
  context = await startVitest('test', [], {
    root: frontend, config: false, run: true, watch: false, envFile: false, include: files,
    environment: 'jsdom', setupFiles: ['./src/testSetup.ts'], cache: false, coverage: { enabled: false }, fileParallelism: false,
    testTimeout: 20000, hookTimeout: 20000, reporters: ['default', 'json'], outputFile: { json: resolve(evidence, 'tests.json') },
    pool: 'threads', poolOptions: { threads: { singleThread: true, execArgv: ['--import', guard] } },
  }, { configFile: false, envFile: false, plugins: [react()], optimizeDeps: { noDiscovery: true, include: [] } })
  assert.ok(context)
  const report = JSON.parse(await readFile(resolve(evidence, 'tests.json'), 'utf8'))
  Object.assign(summary, { passed: report.numPassedTests, failed: report.numFailedTests, pending: report.numPendingTests, total: report.numTotalTests })
  assert.deepEqual(context.state.getUnhandledErrors(), [])
  assert.equal(report.numFailedTests, 0)
  assert.equal(report.numPendingTests, 0)
  assert.ok(report.numPassedTests > 0)
  assert.equal(report.success, true)
  summary.exitCode = 0
} catch (error) { summary.error = String(error); process.exitCode = 1 }
finally {
  if (context) await context.close()
  await writeFile(resolve(evidence, 'summary.json'), JSON.stringify(summary, null, 2))
}
