import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalText } from '../../pages/appShared'
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

const renderSeverityStatus = (severity: string, warningColor: string) => {
  return createElement(Tag, { color: severity === 'Error' ? 'red' : warningColor }, severity)
}

export const renderReadinessFindingSeverityStatus = (severity: string) => {
  return renderSeverityStatus(severity, 'gold')
}

export const renderEvidenceFindingSeverityStatus = (severity: string) => {
  return renderSeverityStatus(severity, 'orange')
}
