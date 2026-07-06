import { describe, expect, it } from 'vitest'

import { buildSelectionKeySet, keepKnownSelectionKeys, normalizeSelectionKeys } from './selectionKeys'

describe('selectionKeys', () => {
  it('builds string selection key sets from entity keys', () => {
    const keys = buildSelectionKeySet([
      { id: 'app-1' },
      { id: 2 },
    ], (item) => item.id)

    expect(keys).toEqual(new Set(['app-1', '2']))
  })

  it('normalizes selected row keys to strings', () => {
    expect(normalizeSelectionKeys(['app-1', 2, 3n])).toEqual(['app-1', '2', '3'])
  })

  it('drops selected keys that are no longer valid', () => {
    const current = ['app-1', 'app-2', 'app-3']

    const next = keepKnownSelectionKeys(current, new Set(['app-1', 'app-3']))

    expect(next).toEqual(['app-1', 'app-3'])
    expect(next).not.toBe(current)
  })

  it('keeps the same selection reference when every key remains valid', () => {
    const current = ['app-1', 'app-2']

    const next = keepKnownSelectionKeys(current, new Set(['app-1', 'app-2', 'app-3']))

    expect(next).toBe(current)
  })
})
