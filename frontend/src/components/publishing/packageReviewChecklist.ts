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

export const buildPackageReviewChecklistRows = ({
  report,
  reportLoaded,
  reportError,
  artifactsError,
  lifecycleIssueCount,
  presentArtifactCount,
  requiredArtifactCount,
}: PackageReviewChecklistInput): PackageReviewChecklistRow[] => [
  {
    key: 'publish-succeeded',
    check: 'Publish succeeded',
    pass: reportLoaded && report?.succeeded === true,
    detail: formatPackageReviewPublishDetail(report?.message, reportError),
  },
  {
    key: 'validation-errors',
    check: 'Validation errors',
    pass: reportLoaded && (report?.errorCount ?? 1) === 0,
    detail: formatPackageReviewChecklistCountDetail(reportLoaded, report?.errorCount, 'error'),
  },
  {
    key: 'lifecycle-issues',
    check: 'Lifecycle issues',
    pass: reportLoaded && lifecycleIssueCount === 0,
    detail: formatPackageReviewChecklistCountDetail(reportLoaded, lifecycleIssueCount, 'issue'),
  },
  {
    key: 'integrity-consistent',
    check: 'Integrity consistent',
    pass: reportLoaded && report?.integritySummary?.isConsistent === true,
    detail: formatPackageReviewIntegrityDetail(report?.integritySummary),
  },
  {
    key: 'required-artifacts-present',
    check: 'Required artifacts present',
    pass: !artifactsError && presentArtifactCount === requiredArtifactCount,
    detail: formatPackageReviewRequiredArtifactsDetail(artifactsError, presentArtifactCount, requiredArtifactCount),
  },
]
