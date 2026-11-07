import './no-delete.mjs'
import { readFile } from 'node:fs/promises'
import { startVitest } from '../../frontend/node_modules/vitest/dist/node.js'
import react from '../../frontend/node_modules/@vitejs/plugin-react/dist/index.js'
const config = JSON.parse(await readFile(new URL('../../job-config.json', import.meta.url)))
const guard = new URL('./no-delete.mjs', import.meta.url).href
const context = await startVitest('test', config.testFiles, {
  root: process.cwd(), config: false, run: true, watch: false,
  environment: 'jsdom', setupFiles: ['./src/testSetup.ts'],
  cache: false, coverage: { enabled: false }, fileParallelism: false,
  testTimeout: 20000, hookTimeout: 20000,
  reporters: ['default', 'json'], outputFile: { json: '../results/tests.json' },
  pool: 'threads', poolOptions: { threads: { singleThread: true, execArgv: ['--import', guard] } },
}, { configFile: false, plugins: [react()], optimizeDeps: { noDiscovery: true, include: [] } })
if (!context) throw new Error('Vitest did not initialize')
await context.close()
