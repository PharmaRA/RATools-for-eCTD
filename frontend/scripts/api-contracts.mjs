import { spawn, spawnSync } from 'node:child_process'
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { createClient } from '@hey-api/openapi-ts'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const frontendDirectory = resolve(scriptDirectory, '..')
const repositoryDirectory = resolve(frontendDirectory, '..')
const apiProjectDirectory = join(repositoryDirectory, 'src', 'RATools.Api')
const apiProjectPath = join(apiProjectDirectory, 'RATools.Api.csproj')
const apiAssemblyPath = join(apiProjectDirectory, 'bin', 'Release', 'net8.0', 'RATools.Api.dll')
const snapshotPath = join(apiProjectDirectory, 'openapi.v1.json')
const generatedDirectory = join(frontendDirectory, 'src', 'api', 'generated')
const mode = process.argv[2]

if (mode !== 'update' && mode !== 'check') {
  throw new Error('Usage: node scripts/api-contracts.mjs <update|check>')
}

const reservePort = async () => new Promise((resolvePort, reject) => {
  const server = createServer()
  server.unref()
  server.on('error', reject)
  server.listen(0, '127.0.0.1', () => {
    const address = server.address()
    if (!address || typeof address === 'string') {
      server.close()
      reject(new Error('Failed to reserve an OpenAPI generation port.'))
      return
    }

    server.close((error) => {
      if (error) reject(error)
      else resolvePort(address.port)
    })
  })
})

const waitForOpenApi = async (url, processOutput) => {
  const deadline = Date.now() + 30_000

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url)
      if (response.ok) return response.text()
    } catch {
      // The local API may still be binding its listener.
    }

    await new Promise((resolveDelay) => setTimeout(resolveDelay, 250))
  }

  throw new Error(`Timed out waiting for the OpenAPI document.\n${processOutput()}`)
}

const updateSnapshot = async () => {
  const build = spawnSync(
    'dotnet',
    ['build', apiProjectPath, '--configuration', 'Release'],
    { cwd: repositoryDirectory, encoding: 'utf8', stdio: 'pipe', windowsHide: true },
  )
  if (build.status !== 0) {
    throw new Error(`Failed to build the API before OpenAPI generation.\n${build.stdout}\n${build.stderr}`)
  }

  const port = await reservePort()
  const output = []
  const apiProcess = spawn('dotnet', [apiAssemblyPath], {
    cwd: apiProjectDirectory,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      Persistence__Provider: 'InMemory',
      Urls: `http://127.0.0.1:${port}`,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  })
  apiProcess.stdout.on('data', (chunk) => output.push(chunk.toString()))
  apiProcess.stderr.on('data', (chunk) => output.push(chunk.toString()))

  try {
    const document = await waitForOpenApi(
      `http://127.0.0.1:${port}/swagger/v1/swagger.json`,
      () => output.join(''),
    )
    const formattedDocument = `${JSON.stringify(JSON.parse(document), null, 2)}\n`
    await writeFile(snapshotPath, formattedDocument, 'utf8')
  } finally {
    apiProcess.kill()
  }
}

const generateTypes = async (outputDirectory) => {
  await createClient({
    input: snapshotPath,
    output: outputDirectory,
    plugins: ['@hey-api/typescript'],
  })
}

const listFiles = async (directory) => {
  const entries = await readdir(directory, { recursive: true, withFileTypes: true })
  return entries
    .filter((entry) => entry.isFile())
    .map((entry) => relative(directory, join(entry.parentPath, entry.name)))
    .sort()
}

const assertGeneratedTypesAreCurrent = async () => {
  const temporaryDirectory = await mkdtemp(join(tmpdir(), 'ratools-api-contracts-'))

  try {
    await generateTypes(temporaryDirectory)
    const expectedFiles = await listFiles(temporaryDirectory)
    const actualFiles = await listFiles(generatedDirectory)
    if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) {
      throw new Error('Generated API contract file list is stale. Run npm run api:generate.')
    }

    for (const file of expectedFiles) {
      const [expected, actual] = await Promise.all([
        readFile(join(temporaryDirectory, file), 'utf8'),
        readFile(join(generatedDirectory, file), 'utf8'),
      ])
      if (actual !== expected) {
        throw new Error(`Generated API contract ${file} is stale. Run npm run api:generate.`)
      }
    }
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true })
  }
}

if (mode === 'update') {
  await updateSnapshot()
  await generateTypes(generatedDirectory)
  console.log('Updated the OpenAPI snapshot and TypeScript contracts.')
} else {
  await assertGeneratedTypesAreCurrent()
  console.log('Generated TypeScript contracts match the OpenAPI snapshot.')
}
