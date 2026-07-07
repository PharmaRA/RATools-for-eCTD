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

export const formatPackageReviewChecklistCountDetail = (
  reportLoaded: boolean,
  count: number | null | undefined,
  label: PackageReviewChecklistCountLabel,
) => (reportLoaded ? `${count ?? '-'} ${label}(s)` : 'Unavailable')

export const formatPackageReviewPublishDetail = (
  reportMessage: string | null | undefined,
  reportError: Error | null,
) => reportMessage || reportError?.message || 'Report unavailable.'

export const formatPackageReviewIntegrityDetail = (
  integritySummary?: PackageReviewIntegritySummary | null,
) => integritySummary?.isConsistent === true ? 'Consistent' : 'Inconsistent or unavailable'

export const formatPackageReviewRequiredArtifactsDetail = (
  artifactsError: Error | null,
  presentArtifactCount: number,
  requiredArtifactCount: number,
) => artifactsError?.message || `${presentArtifactCount}/${requiredArtifactCount} present`

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
    'Publish succeeded',
    reportLoaded && report?.succeeded === true,
    formatPackageReviewPublishDetail(report?.message, reportError),
  ),
  buildPackageReviewChecklistRow(
    'validation-errors',
    'Validation errors',
    reportLoaded && (report?.errorCount ?? 1) === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, report?.errorCount, 'error'),
  ),
  buildPackageReviewChecklistRow(
    'lifecycle-issues',
    'Lifecycle issues',
    reportLoaded && lifecycleIssueCount === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, lifecycleIssueCount, 'issue'),
  ),
  buildPackageReviewChecklistRow(
    'integrity-consistent',
    'Integrity consistent',
    reportLoaded && report?.integritySummary?.isConsistent === true,
    formatPackageReviewIntegrityDetail(report?.integritySummary),
  ),
  buildPackageReviewChecklistRow(
    'required-artifacts-present',
    'Required artifacts present',
    !artifactsError && presentArtifactCount === requiredArtifactCount,
    formatPackageReviewRequiredArtifactsDetail(artifactsError, presentArtifactCount, requiredArtifactCount),
  ),
]
