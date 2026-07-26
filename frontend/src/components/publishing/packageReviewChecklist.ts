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
  error: '个错误',
  issue: '个问题',
}

export const formatPackageReviewChecklistCountDetail = (
  reportLoaded: boolean,
  count: number | null | undefined,
  label: PackageReviewChecklistCountLabel,
) => (reportLoaded ? `${count ?? '-'} ${packageReviewChecklistCountLabels[label]}` : '不可用')

export const formatPackageReviewPublishDetail = (
  reportMessage: string | null | undefined,
  reportError: Error | null,
) => reportMessage || reportError?.message || '报告不可用。'

export const formatPackageReviewIntegrityDetail = (
  integritySummary?: PackageReviewIntegritySummary | null,
) => integritySummary?.isConsistent === true ? '一致' : '不一致或不可用'

export const formatPackageReviewRequiredArtifactsDetail = (
  artifactsError: Error | null,
  presentArtifactCount: number,
  requiredArtifactCount: number,
) => artifactsError?.message || `${presentArtifactCount}/${requiredArtifactCount} 已就绪`

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
    '发布成功',
    reportLoaded && report?.succeeded === true,
    formatPackageReviewPublishDetail(report?.message, reportError),
  ),
  buildPackageReviewChecklistRow(
    'validation-errors',
    '校验错误',
    reportLoaded && (report?.errorCount ?? 1) === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, report?.errorCount, 'error'),
  ),
  buildPackageReviewChecklistRow(
    'lifecycle-issues',
    '生命周期问题',
    reportLoaded && lifecycleIssueCount === 0,
    formatPackageReviewChecklistCountDetail(reportLoaded, lifecycleIssueCount, 'issue'),
  ),
  buildPackageReviewChecklistRow(
    'integrity-consistent',
    '完整性一致',
    reportLoaded && report?.integritySummary?.isConsistent === true,
    formatPackageReviewIntegrityDetail(report?.integritySummary),
  ),
  buildPackageReviewChecklistRow(
    'required-artifacts-present',
    '必需产物齐全',
    !artifactsError && presentArtifactCount === requiredArtifactCount,
    formatPackageReviewRequiredArtifactsDetail(artifactsError, presentArtifactCount, requiredArtifactCount),
  ),
]
