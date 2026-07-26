import { messages } from '../../i18n/messages'
import {
  buildLifecycleIssueCountItems,
  formatOptionalBytes,
  getLifecycleIssueCount,
  type LifecycleSummary,
} from '../../pages/appShared'

export const formatArtifactFileCount = (fileCount?: number | null) => {
  return fileCount == null ? '-' : `${fileCount} files`
}

type ArtifactPackageSummary = {
  packageSizeBytes?: number | null
}

export const formatArtifactPackageSize = (summary?: ArtifactPackageSummary | null) => {
  return summary ? formatOptionalBytes(summary.packageSizeBytes) : null
}

export const formatPublishHistoryLifecycleStatus = (summary?: LifecycleSummary | null) => {
  const issueCount = getLifecycleIssueCount(summary)
  return issueCount === 0
    ? messages.publishHistory.lifecycleAllMatched
    : `${issueCount} ${messages.publishHistory.lifecycleIssueLabel}`
}

type PublishHistoryValidationSummary = {
  errorCount?: number | null
  warningCount?: number | null
}

export const buildPublishHistoryValidationSummaryItems = (
  summary: PublishHistoryValidationSummary,
) => [
  { label: '错误', value: summary.errorCount ?? 0 },
  { label: '警告', value: summary.warningCount ?? 0 },
]

export const formatPublishHistoryStatisticValue = (value?: number | null) => value ?? undefined

type PublishHistoryStatisticDefinition<TSummary> = {
  title: string
  valueKey: keyof TSummary
  color: string
}

export const buildPublishHistoryStatisticItems = <TSummary extends object>(
  summary: TSummary,
  definitions: readonly PublishHistoryStatisticDefinition<TSummary>[],
) => definitions.map(({ title, valueKey, color }) => ({
  title,
  value: formatPublishHistoryStatisticValue(summary[valueKey] as number | null | undefined),
  color,
}))

type PublishHistoryStatusSummary = {
  completedCount?: number | null
  failedCount?: number | null
  runningCount?: number | null
}

export const buildPublishHistoryStatusStatisticItems = (
  summary: PublishHistoryStatusSummary,
) => buildPublishHistoryStatisticItems(summary, [
  { title: '已完成任务', valueKey: 'completedCount', color: '#3f8600' },
  { title: '失败任务', valueKey: 'failedCount', color: '#cf1322' },
  { title: '运行中任务', valueKey: 'runningCount', color: '#1677ff' },
])

type PublishHistoryReadinessSummary = {
  readyCount?: number | null
  blockedCount?: number | null
  unknownCount?: number | null
}

export const buildPublishHistoryReadinessStatisticItems = (
  summary: PublishHistoryReadinessSummary,
) => buildPublishHistoryStatisticItems(summary, [
  { title: '就绪序列', valueKey: 'readyCount', color: '#3f8600' },
  { title: '受阻序列', valueKey: 'blockedCount', color: '#cf1322' },
  { title: '就绪度未知', valueKey: 'unknownCount', color: '#595959' },
])

type PublishHistoryLifecycleStatisticSummary = {
  matchedCount?: number | null
  replaceTargetNotFoundCount?: number | null
  deleteTargetNotFoundCount?: number | null
  appendTargetNotFoundCount?: number | null
  ambiguousCount?: number | null
  currentSequenceCount?: number | null
}

export const buildPublishHistoryLifecycleIssueStatisticItems = (
  summary: PublishHistoryLifecycleStatisticSummary,
) => buildLifecycleIssueCountItems(summary).map(({ label, value }) => ({ title: label, value }))

export const buildPublishHistoryLifecycleStatisticItems = (
  summary: PublishHistoryLifecycleStatisticSummary,
) => [
  { title: '已匹配', value: summary.matchedCount ?? 0 },
  ...buildPublishHistoryLifecycleIssueStatisticItems(summary),
]
