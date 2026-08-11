import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { gzipSync } from 'node:zlib'

const KIB = 1024
const frontendRoot = process.cwd()
const assetsDirectory = path.resolve(frontendRoot, 'dist', 'assets')
const budgetFile = path.resolve(frontendRoot, 'bundle-budget.json')

const formatKiB = (bytes) => `${(bytes / KIB).toFixed(1)} KiB`

const buildMatcher = ({ include, exclude }) => {
  const included = new RegExp(include)
  const excluded = exclude ? new RegExp(exclude) : null
  return (asset) => included.test(asset.name) && !excluded?.test(asset.name)
}

const readAssets = async () => {
  const entries = await readdir(assetsDirectory, { withFileTypes: true })
  const assetEntries = entries.filter((entry) => entry.isFile() && /\.(?:css|js)$/.test(entry.name))

  return Promise.all(assetEntries.map(async (entry) => {
    const contents = await readFile(path.join(assetsDirectory, entry.name))
    return {
      name: entry.name,
      gzipBytes: gzipSync(contents, { level: 9 }).byteLength,
    }
  }))
}

export const validateBudgetDefinition = (budget) => {
  if (!Array.isArray(budget.totals) || !Array.isArray(budget.groups)) {
    throw new Error('Bundle budget must define totals and groups arrays.')
  }

  for (const rule of [...budget.totals, ...budget.groups]) {
    if (!rule.name || !rule.include || !Number.isFinite(rule.maxGzipKiB) || rule.maxGzipKiB <= 0) {
      throw new Error('Every bundle budget rule requires name, include, and a positive maxGzipKiB.')
    }
  }
}

export const checkBudgets = (assets, budget) => {
  const results = []
  const failures = []

  for (const rule of budget.totals) {
    const matchedAssets = assets.filter(buildMatcher(rule))
    const gzipBytes = matchedAssets.reduce((total, asset) => total + asset.gzipBytes, 0)
    const maxGzipBytes = rule.maxGzipKiB * KIB
    const passed = matchedAssets.length > 0 && gzipBytes <= maxGzipBytes

    results.push({ name: rule.name, gzipBytes, maxGzipBytes, passed })
    if (matchedAssets.length === 0) failures.push(`${rule.name} did not match any assets.`)
    if (gzipBytes > maxGzipBytes) failures.push(`${rule.name} exceeds its gzip budget.`)
  }

  const groupMatches = new Map(assets.map((asset) => [asset.name, []]))

  for (const rule of budget.groups) {
    const matchedAssets = assets.filter(buildMatcher(rule))
    const minMatches = rule.minMatches ?? 0
    const maxMatches = rule.maxMatches ?? Number.POSITIVE_INFINITY

    if (matchedAssets.length < minMatches || matchedAssets.length > maxMatches) {
      failures.push(`${rule.name} matched ${matchedAssets.length} assets; expected ${minMatches}-${maxMatches}.`)
    }

    for (const asset of matchedAssets) {
      groupMatches.get(asset.name)?.push(rule.name)
      const maxGzipBytes = rule.maxGzipKiB * KIB
      const passed = asset.gzipBytes <= maxGzipBytes
      results.push({
        name: `${rule.name}: ${asset.name}`,
        gzipBytes: asset.gzipBytes,
        maxGzipBytes,
        passed,
      })
      if (!passed) failures.push(`${asset.name} exceeds the ${rule.name} gzip budget.`)
    }
  }

  for (const [assetName, matchedGroups] of groupMatches) {
    if (matchedGroups.length !== 1) {
      failures.push(`${assetName} must match exactly one chunk group; matched ${matchedGroups.length}.`)
    }
  }

  return { results, failures }
}

const main = async () => {
  const budget = JSON.parse(await readFile(budgetFile, 'utf8'))
  validateBudgetDefinition(budget)

  const assets = await readAssets()
  if (assets.length === 0) {
    throw new Error(`No JavaScript or CSS assets found in ${assetsDirectory}. Run the frontend build first.`)
  }

  const { results, failures } = checkBudgets(assets, budget)
  console.log('Bundle budget (gzip):')
  for (const result of results) {
    const status = result.passed ? 'PASS' : 'FAIL'
    console.log(`  ${status} ${result.name}: ${formatKiB(result.gzipBytes)} / ${formatKiB(result.maxGzipBytes)}`)
  }

  if (failures.length > 0) {
    throw new Error(`Bundle budget failed:\n- ${failures.join('\n- ')}`)
  }
}

const isDirectExecution = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)

if (isDirectExecution) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : error)
    process.exitCode = 1
  })
}
