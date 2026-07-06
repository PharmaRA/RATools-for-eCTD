import { ApiRequestError } from '../../apiClient'

export type PackageReviewArtifact = {
  name: string
  exists?: boolean
  sizeBytes?: number
  contentType?: string
  type?: string
}

export type PackageReviewChecklistRow = {
  key: string
  check: string
  pass: boolean
  detail: string
}

type ChecklistExportRow = {
  key: string
  check: string
  status: 'Pass' | 'Fail'
  detail: string
}

type ReviewExportError = {
  message: string
  status?: number
}

export const buildPackageReviewChecklistExportRows = (
  checklistRows: readonly PackageReviewChecklistRow[],
): ChecklistExportRow[] => checklistRows.map((row) => ({
  key: row.key,
  check: row.check,
  status: row.pass ? 'Pass' : 'Fail',
  detail: row.detail,
}))

export type PublishReadiness = {
  isReady?: boolean
  status?: string
  blockingErrorCount?: number
  warningCount?: number
  missingMetadataFields?: string[]
  categorySummaries?: Array<{
    category: string
    blockingErrorCount: number
    warningCount: number
    findingCount: number
  }>
  findings?: Array<{
    severity: string
    code: string
    category: string
    fieldName?: string | null
    recommendedAction: string
  }>
}

type ValidationLifecycleMatch = {
  resultCode: string
}

export type IntegrityFinding = {
  severity: string
  type: string
  path?: string | null
  message: string
}

export type PackageReviewReport = {
  succeeded?: boolean
  message?: string
  sequenceNumber?: string | null
  validationProfile?: string | null
  errorCount?: number | null
  warningCount?: number | null
  publishJob?: {
    status?: string | null
  }
  validationReport?: {
    lifecycleMatches?: ValidationLifecycleMatch[]
  }
  integritySummary?: {
    isConsistent?: boolean
    missingFilesCount?: number | null
    missingZipEntriesCount?: number | null
    mismatchedArtifactsCount?: number | null
  }
  integrityEvidence?: {
    findings?: IntegrityFinding[]
  }
  publishReadiness?: PublishReadiness | null
}

type PackageReviewExportRequiredArtifact = {
  name: string
  exists?: boolean
  sizeBytes?: number
  contentType?: string
}

export type PackageReviewExportErrors = {
  report?: ReviewExportError
  artifacts?: ReviewExportError
}

export type PackageReviewExportValue = {
  reportVersion: 'package-review-export-v1'
  generatedAtUtc: string
  publishJobId: string
  sequenceNumber: string | null
  validationProfile: string | null
  verdict: 'ReadyForSubmission' | 'NotReadyForSubmission'
  checklist: ChecklistExportRow[]
  riskSummary: {
    validationErrors: number | null
    warnings: number | null
    lifecycleIssues: number | null
    missingFiles: number | null
    missingZipEntries: number | null
    mismatchedArtifacts: number | null
  }
  publishReadiness: {
    isReady: boolean | null
    status: string | null
    blockingErrorCount: number | null
    warningCount: number | null
    missingMetadataFields: string[]
    categorySummaries: NonNullable<PublishReadiness['categorySummaries']>
    findings: Array<{
      severity: string
      code: string
      category: string
      fieldName: string | null
      recommendedAction: string
    }>
  } | null
  requiredArtifacts: Array<{
    name: string
    exists: boolean
    sizeBytes?: number
    contentType?: string
  }>
  integrityFindings: IntegrityFinding[]
  errors?: PackageReviewExportErrors
}

type PublishReadinessFindingExports = NonNullable<PackageReviewExportValue['publishReadiness']>['findings']

export const buildPublishReadinessMissingMetadataFieldExports = (
  publishReadiness?: Pick<PublishReadiness, 'missingMetadataFields'> | null,
): string[] => publishReadiness?.missingMetadataFields || []

export const buildPublishReadinessCategorySummaryExports = (
  publishReadiness?: Pick<PublishReadiness, 'categorySummaries'> | null,
): NonNullable<PublishReadiness['categorySummaries']> => publishReadiness?.categorySummaries || []

export const buildPublishReadinessFindingExports = (
  publishReadiness?: Pick<PublishReadiness, 'findings'> | null,
): PublishReadinessFindingExports => (publishReadiness?.findings || []).map((finding) => ({
  severity: finding.severity,
  code: finding.code,
  category: finding.category,
  fieldName: finding.fieldName ?? null,
  recommendedAction: finding.recommendedAction,
}))

type PackageReviewRequiredArtifactExports = PackageReviewExportValue['requiredArtifacts']

export const buildPackageReviewRequiredArtifactExportRows = (
  requiredArtifactRows: readonly PackageReviewExportRequiredArtifact[],
): PackageReviewRequiredArtifactExports => requiredArtifactRows.map((artifact) => ({
  name: artifact.name,
  exists: artifact.exists === true,
  sizeBytes: artifact.sizeBytes,
  contentType: artifact.contentType,
}))

export const buildPackageReviewExportFilename = (
  sequenceNumber: string | null | undefined,
  jobId: string,
) => `package-review-${sequenceNumber || 'unknown'}-${jobId}.json`

type PackageReviewExportInput = {
  jobId: string
  generatedAtUtc: string
  report: PackageReviewReport | null
  reportLoaded: boolean
  readyForSubmission: boolean
  lifecycleIssueCount: number
  reportError: Error | null
  artifactsError: Error | null
  checklistRows: PackageReviewChecklistRow[]
  requiredArtifactRows: PackageReviewExportRequiredArtifact[]
  integrityFindings: IntegrityFinding[]
}

type PackageReviewRiskSummaryExportInput = Pick<PackageReviewExportInput, 'report' | 'reportLoaded' | 'lifecycleIssueCount'>

type PackageReviewIntegritySummary = NonNullable<PackageReviewReport['integritySummary']>
type PackageReviewIntegrityRiskSummaryExport = Pick<
  PackageReviewExportValue['riskSummary'],
  'missingFiles' | 'missingZipEntries' | 'mismatchedArtifacts'
>

export const buildPackageReviewIntegrityRiskSummaryExport = (
  summary?: PackageReviewIntegritySummary | null,
): PackageReviewIntegrityRiskSummaryExport => ({
  missingFiles: summary?.missingFilesCount ?? null,
  missingZipEntries: summary?.missingZipEntriesCount ?? null,
  mismatchedArtifacts: summary?.mismatchedArtifactsCount ?? null,
})

export const buildPackageReviewRiskSummaryExport = ({
  report,
  reportLoaded,
  lifecycleIssueCount,
}: PackageReviewRiskSummaryExportInput): PackageReviewExportValue['riskSummary'] => ({
  validationErrors: report?.errorCount ?? null,
  warnings: report?.warningCount ?? null,
  lifecycleIssues: reportLoaded ? lifecycleIssueCount : null,
  ...buildPackageReviewIntegrityRiskSummaryExport(report?.integritySummary),
})

const buildErrorExport = (error: Error | null): ReviewExportError | undefined => {
  if (!error) return undefined

  return error instanceof ApiRequestError
    ? { message: error.message, status: error.status }
    : { message: error.message }
}

export const buildPackageReviewExportErrors = ({
  reportError,
  artifactsError,
}: Pick<PackageReviewExportInput, 'reportError' | 'artifactsError'>): PackageReviewExportErrors | undefined => {
  const errors: PackageReviewExportErrors = {}
  const reportExportError = buildErrorExport(reportError)
  const artifactsExportError = buildErrorExport(artifactsError)

  if (reportExportError) {
    errors.report = reportExportError
  }

  if (artifactsExportError) {
    errors.artifacts = artifactsExportError
  }

  return Object.keys(errors).length > 0 ? errors : undefined
}

const buildPublishReadinessExport = (publishReadiness: PublishReadiness | null | undefined): PackageReviewExportValue['publishReadiness'] => {
  if (!publishReadiness) return null

  return {
    isReady: publishReadiness.isReady ?? null,
    status: publishReadiness.status ?? null,
    blockingErrorCount: publishReadiness.blockingErrorCount ?? null,
    warningCount: publishReadiness.warningCount ?? null,
    missingMetadataFields: buildPublishReadinessMissingMetadataFieldExports(publishReadiness),
    categorySummaries: buildPublishReadinessCategorySummaryExports(publishReadiness),
    findings: buildPublishReadinessFindingExports(publishReadiness),
  }
}

export const buildPackageReviewExport = ({
  jobId,
  generatedAtUtc,
  report,
  reportLoaded,
  readyForSubmission,
  lifecycleIssueCount,
  reportError,
  artifactsError,
  checklistRows,
  requiredArtifactRows,
  integrityFindings,
}: PackageReviewExportInput) => {
  const sequenceNumber = report?.sequenceNumber ?? null
  const errors = buildPackageReviewExportErrors({ reportError, artifactsError })

  const value: PackageReviewExportValue = {
    reportVersion: 'package-review-export-v1',
    generatedAtUtc,
    publishJobId: jobId,
    sequenceNumber,
    validationProfile: report?.validationProfile ?? null,
    verdict: readyForSubmission ? 'ReadyForSubmission' : 'NotReadyForSubmission',
    checklist: buildPackageReviewChecklistExportRows(checklistRows),
    riskSummary: buildPackageReviewRiskSummaryExport({ report, reportLoaded, lifecycleIssueCount }),
    publishReadiness: buildPublishReadinessExport(report?.publishReadiness),
    requiredArtifacts: buildPackageReviewRequiredArtifactExportRows(requiredArtifactRows),
    integrityFindings,
  }

  if (errors) {
    value.errors = errors
  }

  return {
    filename: buildPackageReviewExportFilename(sequenceNumber, jobId),
    value,
  }
}
