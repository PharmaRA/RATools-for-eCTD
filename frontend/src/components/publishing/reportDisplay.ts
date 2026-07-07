import { createElement, type ReactNode } from 'react'
import { Tag } from 'antd'

import {
  buildLifecycleIssueCountItems,
  formatDate,
  formatOptionalBytes,
  formatOptionalCount,
  formatOptionalList,
  formatOptionalText,
  getOptionalArray,
} from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { buildEvidenceFindingColumns } from './evidenceFindingDisplay'
import { renderEvidenceFindingSeverityStatus } from './findingSeverityDisplay'
import { buildPublishReadinessCategoryColumns, buildPublishReadinessFindingColumns } from './publishReadinessDisplay'
import { buildIntegrityRiskSummaryItems } from './riskSummaryDisplay'

export const formatReportList = (values?: unknown[]) => formatOptionalList(values)

export const formatReportCount = formatOptionalCount

export const getReportOutcomeDisplayMeta = (succeeded?: boolean) => (
  succeeded
    ? { title: 'Publish Succeeded', iconClassName: 'text-green-500' }
    : { title: 'Publish Failed', iconClassName: 'text-red-500' }
)

export const getReportValidationIssues = <T>(
  report?: { validationReport?: { issues?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.validationReport?.issues)

export const getReportIntegrityFindings = <T>(
  report?: { integrityEvidence?: { findings?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.integrityEvidence?.findings)

export const getReportIntegrityArtifacts = <T>(
  report?: { integrityEvidence?: { artifacts?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.integrityEvidence?.artifacts)

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

type ReportSummaryItemDefinition<TSummary> = {
  key: string
  label: string
  children: (summary: TSummary) => ReactNode
}

export const buildReportSummaryItems = <TSummary>(
  summary: TSummary,
  definitions: readonly ReportSummaryItemDefinition<TSummary>[],
) => definitions.map(({ key, label, children }) => ({
  key,
  label,
  children: children(summary),
}))

type ReportIntegritySummary = {
  isConsistent?: boolean | null
  missingFilesCount?: number | null
  missingZipEntriesCount?: number | null
  mismatchedArtifactsCount?: number | null
}

export const formatReportIntegrityState = (summary: ReportIntegritySummary | null | undefined) => {
  if (!summary) return '-'
  return summary.isConsistent ? 'Consistent' : 'Inconsistent'
}

export const buildReportIntegrityIssueSummaryItems = buildIntegrityRiskSummaryItems

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

export const buildReportArtifactSummaryItems = (
  summary: ReportArtifactSummary | null | undefined,
) => buildReportSummaryItems(summary, [
  { key: 'file-count', label: 'File Count', children: (item) => formatReportCount(item?.fileCount) },
  { key: 'total-size', label: 'Total Size', children: (item) => formatOptionalBytes(item?.totalSizeBytes) },
  { key: 'package-size', label: 'Package Size', children: (item) => formatOptionalBytes(item?.packageSizeBytes) },
])

type ReportAuditSummary = {
  publishJobEventCount?: number | null
  validationEventCount?: number | null
  latestPublishJobAction?: string | null
  latestPublishJobEventUtc?: string | null
}

export const buildReportAuditSummaryItems = (
  summary: ReportAuditSummary | null | undefined,
) => buildReportSummaryItems(summary, [
  { key: 'publish-job-events', label: 'Publish Job Events', children: (item) => formatReportCount(item?.publishJobEventCount) },
  { key: 'validation-events', label: 'Validation Events', children: (item) => formatReportCount(item?.validationEventCount) },
  { key: 'latest-action', label: 'Latest Action', children: (item) => item?.latestPublishJobAction ?? '-' },
  { key: 'latest-event', label: 'Latest Event', children: (item) => formatDate(item?.latestPublishJobEventUtc ?? undefined) },
])

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
) => buildReportSummaryItems(summary, [
  { key: 'issues', label: 'Issues', children: (item) => item.issueCount },
  ...buildLifecycleIssueCountItems(summary).map(({ key, label, value }) => ({
    key,
    label,
    children: () => value,
  })),
])

export const buildReportLifecycleSummaryItems = (
  summary: ReportLifecycleSummary,
  warningSummary?: string | null,
) => [
  { key: 'matched', label: 'Matched', children: summary.matchedCount },
  ...buildReportLifecycleIssueSummaryItems(summary),
  { key: 'warning-summary', label: 'Warning Summary', children: formatOptionalText(warningSummary) },
]

export const renderReportSeverityStatus = renderEvidenceFindingSeverityStatus

export const buildReportValidationIssueColumns = () => [
  { title: 'Severity', dataIndex: 'severity', render: renderReportSeverityStatus, width: 100 },
  { title: 'Code', dataIndex: 'code', width: 200 },
  { title: 'Message', dataIndex: 'message' },
]

export const buildReportIntegrityFindingColumns = () => buildEvidenceFindingColumns({ includeKeys: false })

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

export const buildReportPublishReadinessCategoryColumns = () => (
  buildPublishReadinessCategoryColumns({ categoryWidth: 220, includeKeys: false })
)

export const buildReportPublishReadinessFindingColumns = () => buildPublishReadinessFindingColumns({
  severityRenderer: renderReportSeverityStatus,
  includeKeys: false,
})

export const renderZipEntryPresentStatus = (present?: boolean | null) => {
  if (present === true) return createElement(Tag, { color: 'green' }, 'Present')
  if (present === false) return createElement(Tag, { color: 'red' }, 'Missing from zip')
  return '-'
}
