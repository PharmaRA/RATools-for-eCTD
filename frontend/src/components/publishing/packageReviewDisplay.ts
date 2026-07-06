import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalBytes, formatOptionalText } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { formatReadinessFieldName, getPublishReadinessFindingSeverityTagColor } from './publishReadinessDisplay'
import type { IntegrityFinding, PackageReviewReport } from './packageReviewExport'

type PackageReviewHeaderReport = Pick<PackageReviewReport, 'sequenceNumber' | 'publishJob' | 'validationProfile'>

type PackageReviewRiskSummaryInput = {
  report?: Pick<PackageReviewReport, 'errorCount' | 'warningCount' | 'integritySummary'> | null
  reportLoaded: boolean
  lifecycleIssueCount: number
}

type PackageReviewIntegrityRiskSummary = NonNullable<PackageReviewReport['integritySummary']>

type PackageReviewWarningReport = Pick<PackageReviewReport, 'warningCount'>

type PackageReviewIntegrityEvidenceReport = Pick<PackageReviewReport, 'integrityEvidence'>

export const formatPackageReviewHeaderSummary = (report?: PackageReviewHeaderReport | null) => {
  return `Sequence ${formatOptionalText(report?.sequenceNumber)} | ${formatOptionalText(report?.publishJob?.status)} | ${formatOptionalText(report?.validationProfile)}`
}

const formatPackageReviewRiskCount = (count?: number | null) => count ?? '-'

export const formatPackageReviewWarningAlertDescription = (report?: PackageReviewWarningReport | null) => {
  const warningCount = report?.warningCount ?? 0
  return warningCount > 0 ? `${warningCount} warning(s) remain for reviewer awareness.` : null
}

export const getPackageReviewReadinessDisplayMeta = (readyForSubmission: boolean) => (
  readyForSubmission
    ? { title: 'Ready for Submission', iconClassName: 'text-green-500' }
    : { title: 'Not Ready for Submission', iconClassName: 'text-red-500' }
)

export const getPackageReviewIntegrityFindings = (
  report: PackageReviewIntegrityEvidenceReport | null | undefined,
  reportLoaded: boolean,
): IntegrityFinding[] => reportLoaded ? report?.integrityEvidence?.findings || [] : []

export const buildPackageReviewIntegrityRiskSummaryItems = (
  summary?: PackageReviewIntegrityRiskSummary | null,
) => [
  { key: 'missing-files', label: 'Missing Files', children: formatPackageReviewRiskCount(summary?.missingFilesCount) },
  { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: formatPackageReviewRiskCount(summary?.missingZipEntriesCount) },
  { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: formatPackageReviewRiskCount(summary?.mismatchedArtifactsCount) },
]

export const buildPackageReviewRiskSummaryItems = ({
  report,
  reportLoaded,
  lifecycleIssueCount,
}: PackageReviewRiskSummaryInput) => {
  const loadedReport = reportLoaded ? report : null

  return [
    { key: 'validation-errors', label: 'Validation Errors', children: formatPackageReviewRiskCount(loadedReport?.errorCount) },
    { key: 'warnings', label: 'Warnings', children: formatPackageReviewRiskCount(loadedReport?.warningCount) },
    { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: reportLoaded ? lifecycleIssueCount : '-' },
    ...buildPackageReviewIntegrityRiskSummaryItems(loadedReport?.integritySummary),
  ]
}

export const renderChecklistPassStatus = (pass: boolean) => {
  return createElement(Tag, { color: pass ? 'green' : 'red' }, pass ? 'Pass' : 'Fail')
}

export const buildPackageReviewChecklistColumns = () => [
  { title: 'Check', dataIndex: 'check', key: 'check' },
  { title: 'Status', dataIndex: 'pass', key: 'status', width: 120, render: renderChecklistPassStatus },
  { title: 'Details', dataIndex: 'detail', key: 'detail' },
]

export const buildPackageReviewRequiredArtifactColumns = () => [
  { title: 'Name', dataIndex: 'name', key: 'name', render: (name: string) => createElement('b', null, name) },
  { title: 'Status', dataIndex: 'exists', key: 'status', render: renderArtifactExistsStatus },
  { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: formatOptionalBytes },
  { title: 'Type', dataIndex: 'contentType', key: 'type', render: formatOptionalText },
]

export const renderReadinessFindingSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: getPublishReadinessFindingSeverityTagColor(severity) }, severity)
}

export const buildPackageReviewReadinessFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 120, render: renderReadinessFindingSeverityStatus },
  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
  { title: 'Category', dataIndex: 'category', key: 'category', width: 180 },
  { title: 'Field', dataIndex: 'fieldName', key: 'fieldName', width: 180, render: formatReadinessFieldName },
  { title: 'Recommended Action', dataIndex: 'recommendedAction', key: 'recommendedAction' },
]

export const renderEvidenceFindingSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: severity === 'Error' ? 'red' : 'orange' }, severity)
}

export const buildPackageReviewEvidenceFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 100, render: renderEvidenceFindingSeverityStatus },
  { title: 'Type', dataIndex: 'type', key: 'type', width: 180 },
  { title: 'Path', dataIndex: 'path', key: 'path', width: 260, render: formatOptionalText },
  { title: 'Message', dataIndex: 'message', key: 'message' },
]
