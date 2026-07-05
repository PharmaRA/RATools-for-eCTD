import { describe, expect, it } from 'vitest'

import { keepKnownSelectionKeys } from './selectionKeys'

describe('keepKnownSelectionKeys', () => {
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
