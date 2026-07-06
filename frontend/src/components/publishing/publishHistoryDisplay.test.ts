import { describe, expect, it } from 'vitest'

import {
  buildPublishHistoryLifecycleIssueStatisticItems,
  buildPublishHistoryLifecycleStatisticItems,
  buildPublishHistoryReadinessStatisticItems,
  buildPublishHistoryStatusStatisticItems,
  buildPublishHistoryValidationSummaryItems,
  formatArtifactFileCount,
  formatArtifactPackageSize,
  formatPublishHistoryStatisticValue,
  formatPublishHistoryLifecycleStatus,
} from './publishHistoryDisplay'

describe('publishHistoryDisplay', () => {
  it('formats artifact file counts for publish history rows', () => {
    expect(formatArtifactFileCount(0)).toBe('0 files')
    expect(formatArtifactFileCount(3)).toBe('3 files')
  })

  it('uses a dash when artifact file count is missing', () => {
    expect(formatArtifactFileCount(null)).toBe('-')
    expect(formatArtifactFileCount(undefined)).toBe('-')
  })

  it('formats artifact package size when an artifact summary exists', () => {
    expect(formatArtifactPackageSize(null)).toBeNull()
    expect(formatArtifactPackageSize({ packageSizeBytes: null })).toBe('-')
    expect(formatArtifactPackageSize({ packageSizeBytes: 1536 })).toBe('1.5 KB')
  })

  it('builds validation summary items with zero fallbacks', () => {
    expect(buildPublishHistoryValidationSummaryItems({
      errorCount: 2,
      warningCount: null,
    })).toEqual([
      { label: 'Errors', value: 2 },
      { label: 'Warnings', value: 0 },
    ])
  })

  it('formats publish history statistic values for optional statistic cards', () => {
    expect(formatPublishHistoryStatisticValue(0)).toBe(0)
    expect(formatPublishHistoryStatisticValue(5)).toBe(5)
    expect(formatPublishHistoryStatisticValue(null)).toBeUndefined()
    expect(formatPublishHistoryStatisticValue(undefined)).toBeUndefined()
  })

  it('builds status statistic items with display colors', () => {
    expect(buildPublishHistoryStatusStatisticItems({
      completedCount: 3,
      failedCount: null,
      runningCount: 2,
    })).toEqual([
      { title: 'Completed Jobs', value: 3, color: '#3f8600' },
      { title: 'Failed Jobs', value: undefined, color: '#cf1322' },
      { title: 'Running Jobs', value: 2, color: '#1677ff' },
    ])
  })

  it('builds readiness statistic items with display colors', () => {
    expect(buildPublishHistoryReadinessStatisticItems({
      readyCount: 4,
      blockedCount: null,
      unknownCount: 1,
    })).toEqual([
      { title: 'Ready Sequences', value: 4, color: '#3f8600' },
      { title: 'Blocked Sequences', value: undefined, color: '#cf1322' },
      { title: 'Unknown Readiness', value: 1, color: '#595959' },
    ])
  })

  it('builds lifecycle statistic items with zero fallbacks', () => {
    expect(buildPublishHistoryLifecycleStatisticItems({
      matchedCount: 4,
      replaceTargetNotFoundCount: null,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: undefined,
      ambiguousCount: 1,
      currentSequenceCount: 5,
    })).toEqual([
      { title: 'Matched', value: 4 },
      { title: 'Replace Missing', value: 0 },
      { title: 'Delete Missing', value: 2 },
      { title: 'Append Missing', value: 0 },
      { title: 'Ambiguous', value: 1 },
      { title: 'Current Sequence', value: 5 },
    ])
  })

  it('builds lifecycle issue statistic items with zero fallbacks', () => {
    expect(buildPublishHistoryLifecycleIssueStatisticItems({
      replaceTargetNotFoundCount: null,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: undefined,
      ambiguousCount: 1,
      currentSequenceCount: 5,
    })).toEqual([
      { title: 'Replace Missing', value: 0 },
      { title: 'Delete Missing', value: 2 },
      { title: 'Append Missing', value: 0 },
      { title: 'Ambiguous', value: 1 },
      { title: 'Current Sequence', value: 5 },
    ])
  })

  it('formats lifecycle status text for history rows', () => {
    expect(formatPublishHistoryLifecycleStatus(null)).toBe('All matched')
    expect(formatPublishHistoryLifecycleStatus({
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
    })).toBe('3 issues')
  })
})
