import { createElement } from 'react'
import { Tag } from 'antd'

import { messages } from '../../i18n/messages'
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
  return `序列 ${formatOptionalText(report?.sequenceNumber)} | ${formatOptionalText(report?.publishJob?.status)} | ${formatOptionalText(report?.validationProfile)}`
}

export const formatPackageReviewWarningAlertDescription = (report?: PackageReviewWarningReport | null) => {
  const warningCount = report?.warningCount ?? 0
  return warningCount > 0 ? `仍有 ${warningCount} 个警告需要审阅人关注。` : null
}

export const getPackageReviewReadinessDisplayMeta = (readyForSubmission: boolean) => (
  readyForSubmission
    ? { title: '可提交', iconClassName: 'text-green-500' }
    : { title: '不可提交', iconClassName: 'text-red-500' }
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
    { key: 'validation-errors', label: '校验错误', children: formatRiskSummaryCount(loadedReport?.errorCount) },
    { key: 'warnings', label: '警告', children: formatRiskSummaryCount(loadedReport?.warningCount) },
    { key: 'lifecycle-issues', label: '生命周期问题', children: reportLoaded ? lifecycleIssueCount : '-' },
    ...buildIntegrityRiskSummaryItems(loadedReport?.integritySummary),
  ]
}

export const renderChecklistPassStatus = (pass: boolean) => {
  return createElement(
    Tag,
    { color: pass ? 'green' : 'red' },
    pass ? messages.packageReview.passTag : messages.packageReview.failTag,
  )
}

export const buildPackageReviewChecklistColumns = () => [
  { title: messages.packageReview.columnCheck, dataIndex: 'check', key: 'check' },
  { title: messages.packageReview.columnStatus, dataIndex: 'pass', key: 'status', width: 120, render: renderChecklistPassStatus },
  { title: messages.packageReview.columnDetail, dataIndex: 'detail', key: 'detail' },
]

export const buildPackageReviewRequiredArtifactColumns = () => [
  { title: '名称', dataIndex: 'name', key: 'name', render: (name: string) => createElement('b', null, name) },
  { title: '状态', dataIndex: 'exists', key: 'status', render: renderArtifactExistsStatus },
  { title: '大小', dataIndex: 'sizeBytes', key: 'size', render: formatOptionalBytes },
  { title: '类型', dataIndex: 'contentType', key: 'type', render: formatOptionalText },
]

export const renderReadinessFindingSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: getPublishReadinessFindingSeverityTagColor(severity) }, severity)
}

export const buildPackageReviewReadinessFindingColumns = () => buildPublishReadinessFindingColumns({
  severityRenderer: renderReadinessFindingSeverityStatus,
  severityWidth: 120,
})

export const buildPackageReviewEvidenceFindingColumns = () => buildEvidenceFindingColumns({ typeWidth: 180 })
