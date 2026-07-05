import { describe, expect, it } from 'vitest'

import { buildBatchDeleteState } from './batchDeleteState'

describe('batchDeleteState', () => {
  it('allows batch delete only when selection exists and no delete is running', () => {
    expect(buildBatchDeleteState({
      selectedKeys: ['item-1'],
      deletingKeys: new Set(),
      isBatchDeleteRunning: false,
    })).toEqual({
      hasSingleDeleteRunning: false,
      canStartBatchDelete: true,
    })
  })

  it.each([
    { selectedKeys: [], deletingKeys: new Set<string>(), isBatchDeleteRunning: false },
    { selectedKeys: ['item-1'], deletingKeys: new Set(['item-2']), isBatchDeleteRunning: false },
    { selectedKeys: ['item-1'], deletingKeys: new Set<string>(), isBatchDeleteRunning: true },
  ])('blocks batch delete for unavailable state %#', (input) => {
    expect(buildBatchDeleteState(input).canStartBatchDelete).toBe(false)
  })
})
