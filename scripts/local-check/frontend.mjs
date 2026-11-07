import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { readFile, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { resolve, sep } from 'node:path'
import { startVitest } from '../../frontend/node_modules/vitest/dist/node.js'
import react from '../../frontend/node_modules/@vitejs/plugin-react/dist/index.js'

const root = fileURLToPath(new URL('../../', import.meta.url)), evidence = resolve(process.argv[2])
const frontend = resolve(root, 'frontend')
assert.ok(evidence.startsWith(resolve(root, '.artifacts/local-check') + sep))
const expected = {
  'src/pages/packageValidation/validationReview.test.tsx': 5,
  'src/pages/packageValidation/externalComparison.test.tsx': 11,
  'src/pages/packageValidation/dossierProjection.test.tsx': 3,
  'src/pages/packageValidation/validationDisplay.test.ts': 7,
}
const fingerprints = {}
const summary = { scope: 'Windows jsdom UI contracts; synthetic HTTP, not live browser/API/external qualification',
  task: 'validation-ui', exitCode: 1, category: 'input-failed', node: process.version,
  sourceHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim() }
let context
try {
for (const path of ['package.json', 'package-lock.json', 'src/testSetup.ts', 'src/packageValidationActions.ts', 'src/apiClient.ts',
  'src/pages/packageValidation/ExternalComparisonPanel.tsx', 'src/pages/packageValidation/InputRegistration.tsx',
  'src/pages/packageValidation/FindingLocation.tsx', 'src/pages/packageValidation/DossierProjectionPanel.tsx',
  'src/pages/packageValidation/validationDisplay.ts', ...Object.keys(expected)]) {
  fingerprints[path] = createHash('sha256').update(await readFile(resolve(frontend, path))).digest('hex')
}
await writeFile(resolve(evidence, 'frontend-inputs.json'), JSON.stringify(fingerprints, null, 2))
process.chdir(frontend)
const guard = new URL('../remote-check/no-delete.mjs', import.meta.url).href
summary.category = 'test-failed'
context = await startVitest('test', Object.keys(expected), {
  root: frontend, config: false, run: true, watch: false, envFile: false,
  environment: 'jsdom', setupFiles: ['./src/testSetup.ts'], cache: false, coverage: { enabled: false }, fileParallelism: false,
  testTimeout: 20000, hookTimeout: 20000, reporters: ['default', 'json'], outputFile: { json: resolve(evidence, 'tests.json') },
  pool: 'threads', poolOptions: { threads: { singleThread: true, execArgv: ['--import', guard] } },
}, { configFile: false, envFile: false, plugins: [react()], optimizeDeps: { noDiscovery: true, include: [] } })
if (!context) throw Error('Vitest did not initialize')
summary.category = 'result-validation-failed'
const report = JSON.parse(await readFile(resolve(evidence, 'tests.json'), 'utf8'))
assert.equal(report.testResults.length, Object.keys(expected).length)
for (const [path, count] of Object.entries(expected)) {
  const suite = report.testResults.find(item => item.name.replaceAll('\\', '/') === resolve(frontend, path).replaceAll('\\', '/'))
  assert.ok(suite, `Requested suite was not executed: ${path}`)
  assert.equal(suite.assertionResults.length, count, `Changed case count for ${path}`)
  assert.ok(suite.assertionResults.every(item => item.status === 'passed'), `Failed or unexecuted cases: ${path}`)
}
assert.equal(report.numFailedTests, 0); assert.equal(report.numPendingTests, 0); assert.equal(report.numPassedTests, 26)
Object.assign(summary, { exitCode: 0, category: 'passed', passed: report.numPassedTests, failed: 0, skipped: 0, files: expected })
} catch (error) {
  summary.error = String(error)
  process.exitCode = 1
} finally {
  try { await context?.close() }
  catch (error) { Object.assign(summary, { exitCode: 1, category: 'runner-close-failed', error: String(error) }); process.exitCode = 1 }
  await writeFile(resolve(evidence, 'summary.json'), JSON.stringify(summary, null, 2))
}
