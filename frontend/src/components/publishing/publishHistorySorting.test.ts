import { describe, expect, it } from 'vitest'

import { getPublishHistoryEntriesFromResponse, isReadinessSort, sortPublishHistoryEntries } from './publishHistorySorting'

const entry = (id: string, status?: string | null) => ({
  publishJobId: id,
  publishReadiness: status == null ? null : { status },
})

describe('publishHistorySorting', () => {
  it('reads publish history entries from optional response data', () => {
    const entries = [entry('ready', 'Ready')]

    expect(getPublishHistoryEntriesFromResponse({ entries })).toBe(entries)
    expect(getPublishHistoryEntriesFromResponse({})).toEqual([])
    expect(getPublishHistoryEntriesFromResponse(null)).toEqual([])
    expect(getPublishHistoryEntriesFromResponse(undefined)).toEqual([])
  })

  it('sorts entries by readiness priority without mutating the source list', () => {
    const entries = [
      entry('ready', 'Ready'),
      entry('custom', 'Reviewing'),
      entry('blocked', 'Blocked'),
      entry('unknown', 'Unknown'),
      entry('missing'),
    ]

    expect(sortPublishHistoryEntries(entries, 'blocked-first').map((item) => item.publishJobId)).toEqual([
      'blocked',
      'unknown',
      'missing',
      'ready',
      'custom',
    ])
    expect(sortPublishHistoryEntries(entries, 'ready-first').map((item) => item.publishJobId)).toEqual([
      'custom',
      'ready',
      'unknown',
      'missing',
      'blocked',
    ])
    expect(entries.map((item) => item.publishJobId)).toEqual(['ready', 'custom', 'blocked', 'unknown', 'missing'])
  })

  it('accepts only known readiness sort options', () => {
    expect(isReadinessSort('blocked-first')).toBe(true)
    expect(isReadinessSort('ready-first')).toBe(true)
    expect(isReadinessSort('default')).toBe(false)
  })
})
