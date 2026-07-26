import { messages } from '../../i18n/messages'
import type { PackageReviewChecklistRow, PackageReviewReport } from './packageReviewExport'

type PackageReviewChecklistInput = {
  report: PackageReviewReport | null
  reportLoaded: boolean
  reportError: Error | null
  artifactsError: Error | null
  lifecycleIssueCount: number
  presentArtifactCount: number
  requiredArtifactCount: number
}

type PackageReviewChecklistCountLabel = 'error' | 'issue'
type PackageReviewIntegritySummary = NonNullable<PackageReviewReport['integritySummary']>

const packageReviewChecklistCountLabels: Record<PackageReviewChecklistCountLabel, string> = {
  error: messages.packageReview.errorCountLabel,
  issue: messages.packageReview.issueCountLabel,
}

export const formatPackageReviewChecklistCountDetail = (
  reportLoaded: boolean,
  count: number | null | undefined,
  label: PackageReviewChecklistCountLabel,
) => (reportLoaded ? `${count ?? '-'} ${packageReviewChecklistCountLabels[label]}` : messages.common.unavailable)

export const formatPackageReviewPublishDetail = (
  reportMessage: string | null | undefined,
  reportError: Error | null,
) => reportMessage || reportError?.message || messages.packageReview.reportUnavailable

export const formatPackageReviewIntegrityDetail = (
  integritySummary?: PackageReviewIntegritySummary | null,
) => integritySummary?.isConsistent === true
  ? messages.packageReview.integrityConsistent
  : messages.packageReview.integrityInconsistent

export const formatPackageReviewRequiredArtifactsDetail = (
  artifactsError: Error | null,
  presentArtifactCount: number,
  requiredArtifactCount: number,
) => artifactsError?.message
  || `${presentArtifactCount}/${requiredArtifactCount} ${messages.packageReview.artifactsReadySuffix}`

export const isPackageReviewReadyForSubmission = (rows: readonly PackageReviewChecklistRow[]) => (
  rows.every((row) => row.pass)
)

export const buildPackageReviewChecklistRow = (
  key: string,
  check: string,
  pass: boolean,
  detail: string,
): PackageReviewChecklistRow => ({
  key,
  check,
  pass,
  detail,
})

export const buildPackageReviewChecklistRows = ({
  report,
  reportLoaded,
  reportError,
  artifactsError,
  lifecycleIssueCount,
  presentArtifactCount,
  requiredArtifactCount,
}: PackageReviewChecklistInput): PackageReviewChecklistRow[] => [
  buildPackageReviewChecklistRow(
    'publish-succeeded',
    messages.packageReview.checkPublishSucceeded,
    reportLoaded && report?.succeeded === true,
    formatPackageReviewPublishDetail(report?.message, reportError),
  ),
  buildPackageReviewChecklistRow(
    'validation-errors',
    messages.packageReview.checkValidationErrors,
    reportLoaded && (report?.errorCount ?? 1) === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, report?.errorCount, 'error'),
  ),
  buildPackageReviewChecklistRow(
    'lifecycle-issues',
    messages.packageReview.checkLifecycleIssues,
    reportLoaded && lifecycleIssueCount === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, lifecycleIssueCount, 'issue'),
  ),
  buildPackageReviewChecklistRow(
    'integrity-consistent',
    messages.packageReview.checkIntegrityConsistent,
    reportLoaded && report?.integritySummary?.isConsistent === true,
    formatPackageReviewIntegrityDetail(report?.integritySummary),
  ),
  buildPackageReviewChecklistRow(
    'required-artifacts-present',
    messages.packageReview.checkRequiredArtifacts,
    !artifactsError && presentArtifactCount === requiredArtifactCount,
    formatPackageReviewRequiredArtifactsDetail(artifactsError, presentArtifactCount, requiredArtifactCount),
  ),
]
