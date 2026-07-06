import { describe, expect, it } from 'vitest'

import { buildBatchDeleteSummaryItems } from './batchDeleteDisplay'

describe('batchDeleteDisplay', () => {
  it('builds batch delete summary items from summary counts', () => {
    expect(buildBatchDeleteSummaryItems({
      successCount: 2,
      failureCount: 1,
    })).toEqual([
      { key: 'success', label: '成功', color: 'green', count: 2 },
      { key: 'failure', label: '失败', color: 'red', count: 1 },
    ])
  })

  it('uses zero counts when the batch delete summary is missing', () => {
    expect(buildBatchDeleteSummaryItems(null)).toEqual([
      { key: 'success', label: '成功', color: 'green', count: 0 },
      { key: 'failure', label: '失败', color: 'red', count: 0 },
    ])
  })
})
