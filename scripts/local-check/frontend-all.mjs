import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { readFile, writeFile, readdir } from 'node:fs/promises'
import { resolve, relative, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { gzipSync } from 'node:zlib'
import { startVitest } from '../../frontend/node_modules/vitest/dist/node.js'
import react from '../../frontend/node_modules/@vitejs/plugin-react/dist/index.js'
import { build } from '../../frontend/node_modules/vite/dist/node/index.js'
import ts from '../../frontend/node_modules/typescript/lib/typescript.js'
import { ESLint } from '../../frontend/node_modules/eslint/lib/api.js'
import { checkBudgets, validateBudgetDefinition } from '../../frontend/scripts/check-bundle-budget.mjs'

const root = fileURLToPath(new URL('../../', import.meta.url)), frontend = resolve(root, 'frontend')
const evidence = resolve(process.argv[2]), phase = process.argv[3]
assert.ok(evidence.startsWith(resolve(root, '.artifacts/local-check') + sep))
assert.ok(['tests', 'build', 'lint', 'contracts'].includes(phase))
const hashes = {}, testFiles = []
const hash = bytes => createHash('sha256').update(bytes).digest('hex')
const fingerprint = async path => { hashes[relative(root, path).replaceAll('\\', '/')] = hash(await readFile(path)) }
const summary = { task: ['lint', 'contracts'].includes(phase) ? 'frontend-static' : 'frontend-all', phase, exitCode: 1, node: process.version, typescript: ts.version,
  sourceHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  scope: 'Windows frontend checks with retained dependencies; contracts compare the stored OpenAPI snapshot only; not live API, fresh install or regulatory qualification' }
let context
async function inventory(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name)
    assert.ok(!entry.isSymbolicLink(), 'Frontend input links require explicit review')
    if (entry.isDirectory()) await inventory(path)
    else if (entry.isFile()) {
      await fingerprint(path)
      if (/\.(test|spec)\.[cm]?[jt]sx?$/.test(entry.name)) testFiles.push(relative(frontend, path).replaceAll('\\', '/'))
    }
  }
}
try {
  await inventory(resolve(frontend, 'src')); await inventory(resolve(frontend, 'scripts'))
  for (const file of ['package.json', 'package-lock.json', 'vite.config.ts', 'tsconfig.json', 'tsconfig.app.json', 'tsconfig.node.json',
    'postcss.config.js', 'tailwind.config.js', 'index.html', 'bundle-budget.json', 'eslint.config.js']) await fingerprint(resolve(frontend, file))
  await fingerprint(resolve(root, 'src/RATools.Api/openapi.v1.json'))
  for (const file of ['scripts/local-check/frontend-all.mjs', 'scripts/local-check/frontend-all.ps1', 'scripts/local-check.ps1',
    'scripts/remote-check/no-delete.mjs', 'scripts/local-check/BrowserJob.cs']) await fingerprint(resolve(root, file))
  const tools = {}
  for (const file of ['vitest/package.json', 'vite/package.json', 'typescript/package.json', '@vitejs/plugin-react/package.json', 'eslint/package.json', '@hey-api/openapi-ts/package.json'])
    tools[file] = hash(await readFile(resolve(frontend, 'node_modules', file)))
  tools.nodeExecutable = hash(await readFile(process.execPath))
  summary.tools = tools
  process.chdir(frontend)
  if (phase === 'lint') {
    const eslint = new ESLint({ cwd: frontend, cache: false, fix: false })
    const results = await eslint.lintFiles(['.'])
    await writeFile(resolve(evidence, 'lint.json'), JSON.stringify(results, null, 2))
    summary.files = results.map(result => ({ path: relative(frontend, result.filePath).replaceAll('\\', '/'),
      errors: result.errorCount, warnings: result.warningCount, fatal: result.fatalErrorCount }))
    summary.errors = results.reduce((total, result) => total + result.errorCount, 0)
    summary.warnings = results.reduce((total, result) => total + result.warningCount, 0)
    console.log(await (await eslint.loadFormatter('stylish')).format(results))
    assert.ok(results.length > 0); assert.equal(summary.errors, 0, 'Full frontend lint failed')
  } else if (phase === 'contracts') {
    const output = execFileSync(process.execPath, ['--import', new URL('../remote-check/no-delete.mjs', import.meta.url).href,
      resolve(frontend, 'scripts/api-contracts.mjs'), 'check'], { cwd: frontend, encoding: 'utf8', windowsHide: true })
    console.log(output)
    summary.contractsMatchStoredSnapshot = true
  } else if (phase === 'tests') {
    testFiles.sort(); assert.ok(testFiles.length > 0)
    await writeFile(resolve(evidence, 'test-inventory.json'), JSON.stringify(testFiles, null, 2))
    const guard = new URL('../remote-check/no-delete.mjs', import.meta.url).href
    context = await startVitest('test', [], {
      root: frontend, config: false, run: true, watch: false, envFile: false,
      include: ['src/**/*.{test,spec}.?(c|m)[jt]s?(x)', 'scripts/**/*.{test,spec}.?(c|m)[jt]s?(x)'],
      environment: 'jsdom', setupFiles: ['./src/testSetup.ts'], cache: false, coverage: { enabled: false }, fileParallelism: false,
      testTimeout: 20000, hookTimeout: 20000, reporters: ['default', 'json'], outputFile: { json: resolve(evidence, 'tests.json') },
      pool: 'threads', poolOptions: { threads: { singleThread: true, execArgv: ['--import', guard] } },
    }, { configFile: false, envFile: false, plugins: [react()], optimizeDeps: { noDiscovery: true, include: [] } })
    assert.ok(context, 'Vitest did not initialize')
    const report = JSON.parse(await readFile(resolve(evidence, 'tests.json'), 'utf8'))
    const actualFiles = report.testResults.map(suite => relative(frontend, suite.name).replaceAll('\\', '/')).sort()
    assert.deepEqual(actualFiles, testFiles, 'Executed test files differ from the full source/script inventory')
    summary.files = report.testResults.map(suite => ({ path: relative(frontend, suite.name).replaceAll('\\', '/'),
      passed: suite.assertionResults.filter(test => test.status === 'passed').length,
      failed: suite.assertionResults.filter(test => test.status === 'failed').length, total: suite.assertionResults.length }))
    Object.assign(summary, { passed: report.numPassedTests, failed: report.numFailedTests, pending: report.numPendingTests, total: report.numTotalTests })
    summary.unhandledErrors = context.state.getUnhandledErrors().map(error => String(error?.message ?? error))
    assert.deepEqual(summary.unhandledErrors, [], 'Vitest reported unhandled errors despite passed assertions')
    assert.equal(report.numFailedTests, 0); assert.equal(report.numPendingTests, 0)
    assert.equal(report.numPassedTests, report.numTotalTests); assert.ok(report.numPassedTests > 0)
    assert.ok(report.testResults.every(suite => suite.assertionResults.length > 0 && suite.assertionResults.every(test => test.status === 'passed')))
    assert.equal(report.success, true)
  } else {
    summary.typechecks = []
    for (const name of ['tsconfig.app.json', 'tsconfig.node.json']) {
      const configPath = resolve(frontend, name), raw = ts.readConfigFile(configPath, ts.sys.readFile)
      assert.equal(raw.error, undefined)
      const parsed = ts.parseJsonConfigFileContent(raw.config, ts.sys, frontend, { noEmit: true, incremental: false }, configPath)
      const program = ts.createProgram(parsed.fileNames, parsed.options)
      const diagnostics = [...parsed.errors, ...ts.getPreEmitDiagnostics(program)]
      const text = ts.formatDiagnosticsWithColorAndContext(diagnostics, { getCanonicalFileName: file => file, getCurrentDirectory: () => frontend, getNewLine: () => '\n' })
      await writeFile(resolve(evidence, name + '.log'), text)
      summary.typechecks.push({ config: name, files: parsed.fileNames.length, diagnostics: diagnostics.length })
      assert.equal(diagnostics.length, 0, `${name} typecheck failed; see its log`)
    }
    const configSource = await readFile(resolve(frontend, 'vite.config.ts'), 'utf8')
    const javascript = ts.transpileModule(configSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
      .replace("from 'vite'", `from '${pathToFileURL(resolve(frontend, 'node_modules/vite/dist/node/index.js')).href}'`)
      .replace("from '@vitejs/plugin-react'", `from '${pathToFileURL(resolve(frontend, 'node_modules/@vitejs/plugin-react/dist/index.js')).href}'`)
    const configPath = resolve(evidence, 'production-config.mjs'); await writeFile(configPath, javascript)
    const config = (await import(pathToFileURL(configPath).href)).default
    const outDir = resolve(evidence, 'webroot')
    await build({ ...config, root: frontend, configFile: false, envFile: false, build: { ...config.build, outDir, emptyOutDir: false } })
    const assets = []
    for (const entry of await readdir(resolve(outDir, 'assets'), { withFileTypes: true })) {
      if (entry.isFile() && /\.(css|js)$/.test(entry.name)) {
        const bytes = await readFile(resolve(outDir, 'assets', entry.name))
        assets.push({ name: entry.name, sha256: hash(bytes), gzipBytes: gzipSync(bytes, { level: 9 }).byteLength })
      }
    }
    assert.ok(assets.length > 0)
    const budget = JSON.parse(await readFile(resolve(frontend, 'bundle-budget.json'), 'utf8')); validateBudgetDefinition(budget)
    summary.budget = checkBudgets(assets, budget)
    await writeFile(resolve(evidence, 'assets.json'), JSON.stringify(assets, null, 2))
    assert.deepEqual(summary.budget.failures, [], 'Original production budget failed')
  }
  summary.exitCode = 0
} catch (error) { summary.error = String(error); process.exitCode = 1; console.log(summary.error) }
finally {
  try { await context?.close() } catch (error) { summary.exitCode = 1; summary.error = String(error); process.exitCode = 1 }
  if (context) {
    summary.unhandledErrors = context.state.getUnhandledErrors().map(error => String(error?.message ?? error))
    if (summary.unhandledErrors.length || process.exitCode) {
      summary.exitCode = 1; process.exitCode = 1
      summary.error ??= 'Vitest unhandled errors or nonzero process exit state'
    }
  }
  const manifest = JSON.stringify(hashes, null, 2)
  summary.sourceManifestSha256 = hash(manifest)
  await writeFile(resolve(evidence, phase + '-inputs.json'), manifest)
  await writeFile(resolve(evidence, phase + '-summary.json'), JSON.stringify(summary, null, 2))
}
