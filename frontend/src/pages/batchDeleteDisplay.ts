import type { BatchDeleteSummary } from '../deleteActions'

type BatchDeleteSummaryCounts = Pick<BatchDeleteSummary, 'successCount' | 'failureCount'>

export const buildBatchDeleteSummaryItems = (
  summary?: BatchDeleteSummaryCounts | null,
) => [
  { key: 'success', label: '成功', color: 'green', count: summary?.successCount ?? 0 },
  { key: 'failure', label: '失败', color: 'red', count: summary?.failureCount ?? 0 },
]
