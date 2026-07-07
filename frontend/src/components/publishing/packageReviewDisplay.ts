import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalBytes, formatOptionalText } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { buildEvidenceFindingColumns } from './evidenceFindingDisplay'
import { buildPublishReadinessFindingColumns, getPublishReadinessFindingSeverityTagColor } from './publishReadinessDisplay'
import { buildIntegrityRiskSummaryItems, formatRiskSummaryCount } from './riskSummaryDisplay'
import type { IntegrityFinding, PackageReviewReport } from './packageReviewExport'

export { renderEvidenceFindingSeverityStatus } from './findingSeverityDisplay'
export {
  buildIntegrityRiskSummaryItems,
  buildIntegrityRiskSummaryItems as buildPackageReviewIntegrityRiskSummaryItems,
} from './riskSummaryDisplay'

type PackageReviewHeaderReport = Pick<PackageReviewReport, 'sequenceNumber' | 'publishJob' | 'validationProfile'>

type PackageReviewRiskSummaryInput = {
  report?: Pick<PackageReviewReport, 'errorCount' | 'warningCount' | 'integritySummary'> | null
  reportLoaded: boolean
  lifecycleIssueCount: number
}

type PackageReviewWarningReport = Pick<PackageReviewReport, 'warningCount'>

type PackageReviewIntegrityEvidenceReport = Pick<PackageReviewReport, 'integrityEvidence'>

export const formatPackageReviewHeaderSummary = (report?: PackageReviewHeaderReport | null) => {
  return `Sequence ${formatOptionalText(report?.sequenceNumber)} | ${formatOptionalText(report?.publishJob?.status)} | ${formatOptionalText(report?.validationProfile)}`
}

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

export const buildPackageReviewRiskSummaryItems = ({
  report,
  reportLoaded,
  lifecycleIssueCount,
}: PackageReviewRiskSummaryInput) => {
  const loadedReport = reportLoaded ? report : null

  return [
    { key: 'validation-errors', label: 'Validation Errors', children: formatRiskSummaryCount(loadedReport?.errorCount) },
    { key: 'warnings', label: 'Warnings', children: formatRiskSummaryCount(loadedReport?.warningCount) },
    { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: reportLoaded ? lifecycleIssueCount : '-' },
    ...buildIntegrityRiskSummaryItems(loadedReport?.integritySummary),
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

export const buildPackageReviewReadinessFindingColumns = () => buildPublishReadinessFindingColumns({
  severityRenderer: renderReadinessFindingSeverityStatus,
  severityWidth: 120,
})

export const buildPackageReviewEvidenceFindingColumns = () => buildEvidenceFindingColumns({ typeWidth: 180 })
