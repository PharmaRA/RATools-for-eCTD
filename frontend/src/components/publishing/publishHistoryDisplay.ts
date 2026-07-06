import {
  formatOptionalBytes,
  getLifecycleIssueCount,
  getLifecycleIssueCountValues,
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

type PublishHistoryStatusSummary = {
  completedCount?: number | null
  failedCount?: number | null
  runningCount?: number | null
}

export const buildPublishHistoryStatusStatisticItems = (
  summary: PublishHistoryStatusSummary,
) => [
  { title: 'Completed Jobs', value: summary.completedCount ?? undefined, color: '#3f8600' },
  { title: 'Failed Jobs', value: summary.failedCount ?? undefined, color: '#cf1322' },
  { title: 'Running Jobs', value: summary.runningCount ?? undefined, color: '#1677ff' },
]

type PublishHistoryReadinessSummary = {
  readyCount?: number | null
  blockedCount?: number | null
  unknownCount?: number | null
}

export const buildPublishHistoryReadinessStatisticItems = (
  summary: PublishHistoryReadinessSummary,
) => [
  { title: 'Ready Sequences', value: summary.readyCount ?? undefined, color: '#3f8600' },
  { title: 'Blocked Sequences', value: summary.blockedCount ?? undefined, color: '#cf1322' },
  { title: 'Unknown Readiness', value: summary.unknownCount ?? undefined, color: '#595959' },
]

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
) => {
  const [
    replaceTargetNotFoundCount,
    deleteTargetNotFoundCount,
    appendTargetNotFoundCount,
    ambiguousCount,
    currentSequenceCount,
  ] = getLifecycleIssueCountValues(summary)

  return [
    { title: 'Replace Missing', value: replaceTargetNotFoundCount },
    { title: 'Delete Missing', value: deleteTargetNotFoundCount },
    { title: 'Append Missing', value: appendTargetNotFoundCount },
    { title: 'Ambiguous', value: ambiguousCount },
    { title: 'Current Sequence', value: currentSequenceCount },
  ]
}

export const buildPublishHistoryLifecycleStatisticItems = (
  summary: PublishHistoryLifecycleStatisticSummary,
) => [
  { title: 'Matched', value: summary.matchedCount ?? 0 },
  ...buildPublishHistoryLifecycleIssueStatisticItems(summary),
]
