import { describe, expect, it } from 'vitest'

import { checkBudgets, validateBudgetDefinition } from './check-bundle-budget.mjs'

const budget = {
  totals: [
    { name: 'JavaScript total', include: '\\.js$', maxGzipKiB: 2 },
    { name: 'Stylesheet total', include: '\\.css$', maxGzipKiB: 1 },
  ],
  groups: [
    {
      name: 'Vendor',
      include: '^vendor-.*\\.js$',
      maxGzipKiB: 1,
      minMatches: 1,
      maxMatches: 1,
    },
    {
      name: 'Application',
      include: '\\.js$',
      exclude: '^vendor-',
      maxGzipKiB: 1,
      minMatches: 1,
    },
    { name: 'Stylesheets', include: '\\.css$', maxGzipKiB: 1, minMatches: 1 },
  ],
}

describe('bundle budget checker', () => {
  it('accepts assets within the configured totals and chunk groups', () => {
    validateBudgetDefinition(budget)

    const result = checkBudgets([
      { name: 'vendor-abc.js', gzipBytes: 900 },
      { name: 'index-abc.js', gzipBytes: 700 },
      { name: 'index-abc.css', gzipBytes: 500 },
    ], budget)

    expect(result.failures).toEqual([])
    expect(result.results.every((entry) => entry.passed)).toBe(true)
  })

  it('rejects over-budget and ungrouped assets', () => {
    const result = checkBudgets([
      { name: 'vendor-abc.js', gzipBytes: 1100 },
      { name: 'index-abc.js', gzipBytes: 1100 },
      { name: 'index-abc.css', gzipBytes: 500 },
      { name: 'unbudgeted.svg', gzipBytes: 10 },
    ], budget)

    expect(result.failures).toContain('vendor-abc.js exceeds the Vendor gzip budget.')
    expect(result.failures).toContain('index-abc.js exceeds the Application gzip budget.')
    expect(result.failures).toContain('unbudgeted.svg must match exactly one chunk group; matched 0.')
  })
})
