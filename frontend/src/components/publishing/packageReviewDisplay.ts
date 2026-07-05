import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalBytes, formatOptionalText } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { formatReadinessFieldName } from './publishReadinessDisplay'
import type { PackageReviewReport } from './packageReviewExport'

type PackageReviewHeaderReport = Pick<PackageReviewReport, 'sequenceNumber' | 'publishJob' | 'validationProfile'>

type PackageReviewRiskSummaryInput = {
  report?: Pick<PackageReviewReport, 'errorCount' | 'warningCount' | 'integritySummary'> | null
  reportLoaded: boolean
  lifecycleIssueCount: number
}

type PackageReviewWarningReport = Pick<PackageReviewReport, 'warningCount'>

export const formatPackageReviewHeaderSummary = (report?: PackageReviewHeaderReport | null) => {
  return `Sequence ${formatOptionalText(report?.sequenceNumber)} | ${formatOptionalText(report?.publishJob?.status)} | ${formatOptionalText(report?.validationProfile)}`
}

const formatPackageReviewRiskCount = (count?: number | null) => count ?? '-'

export const formatPackageReviewWarningAlertDescription = (report?: PackageReviewWarningReport | null) => {
  const warningCount = report?.warningCount ?? 0
  return warningCount > 0 ? `${warningCount} warning(s) remain for reviewer awareness.` : null
}

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
    { key: 'missing-files', label: 'Missing Files', children: formatPackageReviewRiskCount(loadedReport?.integritySummary?.missingFilesCount) },
    { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: formatPackageReviewRiskCount(loadedReport?.integritySummary?.missingZipEntriesCount) },
    { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: formatPackageReviewRiskCount(loadedReport?.integritySummary?.mismatchedArtifactsCount) },
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

const renderSeverityStatus = (severity: string, warningColor: string) => {
  return createElement(Tag, { color: severity === 'Error' ? 'red' : warningColor }, severity)
}

export const renderReadinessFindingSeverityStatus = (severity: string) => {
  return renderSeverityStatus(severity, 'gold')
}

export const buildPackageReviewReadinessFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 120, render: renderReadinessFindingSeverityStatus },
  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
  { title: 'Category', dataIndex: 'category', key: 'category', width: 180 },
  { title: 'Field', dataIndex: 'fieldName', key: 'fieldName', width: 180, render: formatReadinessFieldName },
  { title: 'Recommended Action', dataIndex: 'recommendedAction', key: 'recommendedAction' },
]

export const renderEvidenceFindingSeverityStatus = (severity: string) => {
  return renderSeverityStatus(severity, 'orange')
}

export const buildPackageReviewEvidenceFindingColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 100, render: renderEvidenceFindingSeverityStatus },
  { title: 'Type', dataIndex: 'type', key: 'type', width: 180 },
  { title: 'Path', dataIndex: 'path', key: 'path', width: 260, render: formatOptionalText },
  { title: 'Message', dataIndex: 'message', key: 'message' },
]
