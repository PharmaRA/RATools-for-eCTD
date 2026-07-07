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
  return issueCount === 0 ? 'All matched' : `${issueCount} issues`
}

type PublishHistoryValidationSummary = {
  errorCount?: number | null
  warningCount?: number | null
}

export const buildPublishHistoryValidationSummaryItems = (
  summary: PublishHistoryValidationSummary,
) => [
  { label: 'Errors', value: summary.errorCount ?? 0 },
  { label: 'Warnings', value: summary.warningCount ?? 0 },
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
  { title: 'Completed Jobs', valueKey: 'completedCount', color: '#3f8600' },
  { title: 'Failed Jobs', valueKey: 'failedCount', color: '#cf1322' },
  { title: 'Running Jobs', valueKey: 'runningCount', color: '#1677ff' },
])

type PublishHistoryReadinessSummary = {
  readyCount?: number | null
  blockedCount?: number | null
  unknownCount?: number | null
}

export const buildPublishHistoryReadinessStatisticItems = (
  summary: PublishHistoryReadinessSummary,
) => buildPublishHistoryStatisticItems(summary, [
  { title: 'Ready Sequences', valueKey: 'readyCount', color: '#3f8600' },
  { title: 'Blocked Sequences', valueKey: 'blockedCount', color: '#cf1322' },
  { title: 'Unknown Readiness', valueKey: 'unknownCount', color: '#595959' },
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
  { title: 'Matched', value: summary.matchedCount ?? 0 },
  ...buildPublishHistoryLifecycleIssueStatisticItems(summary),
]
