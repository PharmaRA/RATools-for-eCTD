import { describe, expect, it } from 'vitest'

import { buildLifecycleIssueCountItems } from '../../pages/appShared'
import {
  buildPublishHistoryLifecycleIssueStatisticItems,
  buildPublishHistoryLifecycleStatisticItems,
  buildPublishHistoryReadinessStatisticItems,
  buildPublishHistoryStatisticItems,
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
      { label: '错误', value: 2 },
      { label: '警告', value: 0 },
    ])
  })

  it('formats publish history statistic values for optional statistic cards', () => {
    expect(formatPublishHistoryStatisticValue(0)).toBe(0)
    expect(formatPublishHistoryStatisticValue(5)).toBe(5)
    expect(formatPublishHistoryStatisticValue(null)).toBeUndefined()
    expect(formatPublishHistoryStatisticValue(undefined)).toBeUndefined()
  })

  it('builds statistic items from summary keys and display definitions', () => {
    expect(buildPublishHistoryStatisticItems(
      { completedCount: 3, failedCount: null },
      [
        { title: '已完成任务', valueKey: 'completedCount', color: '#3f8600' },
        { title: '失败任务', valueKey: 'failedCount', color: '#cf1322' },
      ],
    )).toEqual([
      { title: '已完成任务', value: 3, color: '#3f8600' },
      { title: '失败任务', value: undefined, color: '#cf1322' },
    ])
  })

  it('builds status statistic items with display colors', () => {
    expect(buildPublishHistoryStatusStatisticItems({
      completedCount: 3,
      failedCount: null,
      runningCount: 2,
    })).toEqual([
      { title: '已完成任务', value: 3, color: '#3f8600' },
      { title: '失败任务', value: undefined, color: '#cf1322' },
      { title: '运行中任务', value: 2, color: '#1677ff' },
    ])
  })

  it('builds readiness statistic items with display colors', () => {
    expect(buildPublishHistoryReadinessStatisticItems({
      readyCount: 4,
      blockedCount: null,
      unknownCount: 1,
    })).toEqual([
      { title: '就绪序列', value: 4, color: '#3f8600' },
      { title: '受阻序列', value: undefined, color: '#cf1322' },
      { title: '就绪度未知', value: 1, color: '#595959' },
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
      { title: '已匹配', value: 4 },
      { title: '替换目标缺失', value: 0 },
      { title: '删除目标缺失', value: 2 },
      { title: '追加目标缺失', value: 0 },
      { title: '存在歧义', value: 1 },
      { title: '当前序列', value: 5 },
    ])
  })

  it('builds lifecycle issue statistic items with zero fallbacks', () => {
    const summary = {
      replaceTargetNotFoundCount: null,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: undefined,
      ambiguousCount: 1,
      currentSequenceCount: 5,
    }

    expect(buildPublishHistoryLifecycleIssueStatisticItems(summary)).toEqual([
      { title: '替换目标缺失', value: 0 },
      { title: '删除目标缺失', value: 2 },
      { title: '追加目标缺失', value: 0 },
      { title: '存在歧义', value: 1 },
      { title: '当前序列', value: 5 },
    ])
  })

  it('builds lifecycle issue statistic items from shared lifecycle issue counts', () => {
    const summary = {
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: 3,
      ambiguousCount: 4,
      currentSequenceCount: 5,
    }

    expect(buildPublishHistoryLifecycleIssueStatisticItems(summary)).toEqual(
      buildLifecycleIssueCountItems(summary).map(({ label, value }) => ({ title: label, value })),
    )
  })

  it('formats lifecycle status text for history rows', () => {
    expect(formatPublishHistoryLifecycleStatus(null)).toBe('全部匹配')
    expect(formatPublishHistoryLifecycleStatus({
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
    })).toBe('3 个问题')
  })
})
