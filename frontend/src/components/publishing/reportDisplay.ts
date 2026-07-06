import { createElement } from 'react'
import { Tag } from 'antd'

import { formatDate, formatOptionalBytes, formatOptionalText } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { formatReadinessFieldName } from './publishReadinessDisplay'

export const formatReportList = (values?: unknown[]) => values?.length ? values.join(', ') : '-'

export const formatReportCount = (count?: number | null) => count ?? '-'

export const getReportOutcomeDisplayMeta = (succeeded?: boolean) => (
  succeeded
    ? { title: 'Publish Succeeded', iconClassName: 'text-green-500' }
    : { title: 'Publish Failed', iconClassName: 'text-red-500' }
)

export const getReportValidationIssues = <T>(
  report?: { validationReport?: { issues?: T[] | null } | null } | null,
): T[] => report?.validationReport?.issues || []

export const getReportIntegrityFindings = <T>(
  report?: { integrityEvidence?: { findings?: T[] | null } | null } | null,
): T[] => report?.integrityEvidence?.findings || []

export const getReportIntegrityArtifacts = <T>(
  report?: { integrityEvidence?: { artifacts?: T[] | null } | null } | null,
): T[] => report?.integrityEvidence?.artifacts || []

type ReportOverview = {
  validationProfile?: string
  durationMs?: number
  errorCount?: number
  warningCount?: number
}

export const buildReportOverviewItems = (
  report: ReportOverview,
  lifecycleIssueCount: number,
  integrityState: string,
) => [
  { key: 'profile', label: 'Profile', children: report.validationProfile },
  { key: 'duration', label: 'Duration', children: `${report.durationMs} ms` },
  { key: 'errors', label: 'Errors', children: report.errorCount },
  { key: 'warnings', label: 'Warnings', children: report.warningCount },
  { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: lifecycleIssueCount },
  { key: 'integrity', label: 'Integrity', children: integrityState },
]

type ReportIntegritySummary = {
  isConsistent?: boolean | null
  missingFilesCount?: number | null
  missingZipEntriesCount?: number | null
  mismatchedArtifactsCount?: number | null
}

type ReportIntegrityIssueSummary = Pick<
  ReportIntegritySummary,
  'missingFilesCount' | 'missingZipEntriesCount' | 'mismatchedArtifactsCount'
>

export const formatReportIntegrityState = (summary: ReportIntegritySummary | null | undefined) => {
  if (!summary) return '-'
  return summary.isConsistent ? 'Consistent' : 'Inconsistent'
}

export const buildReportIntegrityIssueSummaryItems = (
  summary: ReportIntegrityIssueSummary | null | undefined,
) => [
  { key: 'missing-files', label: 'Missing Files', children: formatReportCount(summary?.missingFilesCount) },
  { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: formatReportCount(summary?.missingZipEntriesCount) },
  { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: formatReportCount(summary?.mismatchedArtifactsCount) },
]

export const buildReportIntegritySummaryItems = (
  summary: ReportIntegritySummary | null | undefined,
  integrityState: string,
) => [
  { key: 'consistent', label: 'Consistent', children: integrityState },
  ...buildReportIntegrityIssueSummaryItems(summary),
]

type ReportArtifactSummary = {
  fileCount?: number | null
  totalSizeBytes?: number | null
  packageSizeBytes?: number | null
}

export const buildReportArtifactSummaryItems = (summary: ReportArtifactSummary | null | undefined) => [
  { key: 'file-count', label: 'File Count', children: formatReportCount(summary?.fileCount) },
  { key: 'total-size', label: 'Total Size', children: formatOptionalBytes(summary?.totalSizeBytes) },
  { key: 'package-size', label: 'Package Size', children: formatOptionalBytes(summary?.packageSizeBytes) },
]

type ReportAuditSummary = {
  publishJobEventCount?: number | null
  validationEventCount?: number | null
  latestPublishJobAction?: string | null
  latestPublishJobEventUtc?: string | null
}

export const buildReportAuditSummaryItems = (summary: ReportAuditSummary | null | undefined) => [
  { key: 'publish-job-events', label: 'Publish Job Events', children: formatReportCount(summary?.publishJobEventCount) },
  { key: 'validation-events', label: 'Validation Events', children: formatReportCount(summary?.validationEventCount) },
  { key: 'latest-action', label: 'Latest Action', children: summary?.latestPublishJobAction ?? '-' },
  { key: 'latest-event', label: 'Latest Event', children: formatDate(summary?.latestPublishJobEventUtc ?? undefined) },
]

type ReportLifecycleIssueSummary = {
  issueCount: number
  replaceTargetNotFoundCount: number
  deleteTargetNotFoundCount: number
  appendTargetNotFoundCount: number
  ambiguousCount: number
  currentSequenceCount: number
}

type ReportLifecycleSummary = ReportLifecycleIssueSummary & {
  matchedCount: number
}

export const buildReportLifecycleIssueSummaryItems = (
  summary: ReportLifecycleIssueSummary,
) => [
  { key: 'issues', label: 'Issues', children: summary.issueCount },
  { key: 'replace-missing', label: 'Replace Missing', children: summary.replaceTargetNotFoundCount },
  { key: 'delete-missing', label: 'Delete Missing', children: summary.deleteTargetNotFoundCount },
  { key: 'append-missing', label: 'Append Missing', children: summary.appendTargetNotFoundCount },
  { key: 'ambiguous', label: 'Ambiguous', children: summary.ambiguousCount },
  { key: 'current-sequence', label: 'Current Sequence', children: summary.currentSequenceCount },
]

export const buildReportLifecycleSummaryItems = (
  summary: ReportLifecycleSummary,
  warningSummary?: string | null,
) => [
  { key: 'matched', label: 'Matched', children: summary.matchedCount },
  ...buildReportLifecycleIssueSummaryItems(summary),
  { key: 'warning-summary', label: 'Warning Summary', children: formatOptionalText(warningSummary) },
]

export const renderReportSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: severity === 'Error' ? 'red' : 'orange' }, severity)
}

export const buildReportValidationIssueColumns = () => [
  { title: 'Severity', dataIndex: 'severity', render: renderReportSeverityStatus, width: 100 },
  { title: 'Code', dataIndex: 'code', width: 200 },
  { title: 'Message', dataIndex: 'message' },
]

export const buildReportIntegrityFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', width: 100, render: renderReportSeverityStatus },
  { title: 'Type', dataIndex: 'type', width: 200 },
  { title: 'Path', dataIndex: 'path', width: 260, render: formatOptionalText },
  { title: 'Message', dataIndex: 'message' },
]

export const buildReportArtifactManifestColumns = () => [
  { title: 'Role', dataIndex: 'role', width: 140 },
  { title: 'Relative Path', dataIndex: 'relativePath', width: 260, render: formatOptionalText },
  { title: 'Exists', dataIndex: 'exists', width: 120, render: renderArtifactExistsStatus },
  { title: 'Size', dataIndex: 'sizeBytes', width: 120, render: formatOptionalBytes },
  { title: 'Zip Entry', dataIndex: 'zipEntryPresent', width: 150, render: renderZipEntryPresentStatus },
  { title: 'Source', dataIndex: 'source', width: 160 },
]

export const buildReportLifecycleMatchColumns = () => [
  { title: 'Operation', dataIndex: 'operation', width: 120 },
  { title: 'Sequence', dataIndex: 'sequenceNumber', width: 100 },
  { title: 'CTD Section', dataIndex: 'ctdSection', width: 120 },
  { title: 'Document ID', dataIndex: 'documentId', width: 180 },
  { title: 'Result Code', dataIndex: 'resultCode', width: 240 },
  { title: 'Match Strategy', dataIndex: 'matchStrategy', width: 180 },
  { title: 'Attempted Strategies', dataIndex: 'attemptedStrategies', render: formatReportList, width: 220 },
  { title: 'Historical Matches', dataIndex: 'historicalMatchCount', width: 140 },
  { title: 'Historical Sequences', dataIndex: 'historicalSequenceNumbers', render: formatReportList, width: 180 },
  { title: 'Historical Placement IDs', dataIndex: 'historicalPlacementIds', render: formatReportList, width: 240 },
  { title: 'Final State', dataIndex: 'historicalFinalState', width: 140 },
]

export const buildReportPublishReadinessCategoryColumns = () => [
  { title: 'Category', dataIndex: 'category', width: 220 },
  { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', width: 140 },
  { title: 'Warnings', dataIndex: 'warningCount', width: 120 },
  { title: 'Findings', dataIndex: 'findingCount', width: 120 },
]

export const buildReportPublishReadinessFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', width: 100, render: renderReportSeverityStatus },
  { title: 'Code', dataIndex: 'code', width: 220 },
  { title: 'Category', dataIndex: 'category', width: 180 },
  { title: 'Field', dataIndex: 'fieldName', width: 180, render: formatReadinessFieldName },
  { title: 'Recommended Action', dataIndex: 'recommendedAction' },
]

export const renderZipEntryPresentStatus = (present?: boolean | null) => {
  if (present === true) return createElement(Tag, { color: 'green' }, 'Present')
  if (present === false) return createElement(Tag, { color: 'red' }, 'Missing from zip')
  return '-'
}
