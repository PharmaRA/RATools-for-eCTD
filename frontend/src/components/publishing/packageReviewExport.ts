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

type PackageReviewExportErrors = {
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

const buildErrorExport = (error: Error | null): ReviewExportError | undefined => {
  if (!error) return undefined

  return error instanceof ApiRequestError
    ? { message: error.message, status: error.status }
    : { message: error.message }
}

const buildPublishReadinessExport = (publishReadiness: PublishReadiness | null | undefined): PackageReviewExportValue['publishReadiness'] => {
  if (!publishReadiness) return null

  return {
    isReady: publishReadiness.isReady ?? null,
    status: publishReadiness.status ?? null,
    blockingErrorCount: publishReadiness.blockingErrorCount ?? null,
    warningCount: publishReadiness.warningCount ?? null,
    missingMetadataFields: publishReadiness.missingMetadataFields || [],
    categorySummaries: publishReadiness.categorySummaries || [],
    findings: (publishReadiness.findings || []).map((finding) => ({
      severity: finding.severity,
      code: finding.code,
      category: finding.category,
      fieldName: finding.fieldName ?? null,
      recommendedAction: finding.recommendedAction,
    })),
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
  const errors: PackageReviewExportErrors = {}
  const reportExportError = buildErrorExport(reportError)
  const artifactsExportError = buildErrorExport(artifactsError)

  if (reportExportError) {
    errors.report = reportExportError
  }

  if (artifactsExportError) {
    errors.artifacts = artifactsExportError
  }

  const value: PackageReviewExportValue = {
    reportVersion: 'package-review-export-v1',
    generatedAtUtc,
    publishJobId: jobId,
    sequenceNumber,
    validationProfile: report?.validationProfile ?? null,
    verdict: readyForSubmission ? 'ReadyForSubmission' : 'NotReadyForSubmission',
    checklist: checklistRows.map((row) => ({
      key: row.key,
      check: row.check,
      status: row.pass ? 'Pass' : 'Fail',
      detail: row.detail,
    })),
    riskSummary: {
      validationErrors: report?.errorCount ?? null,
      warnings: report?.warningCount ?? null,
      lifecycleIssues: reportLoaded ? lifecycleIssueCount : null,
      missingFiles: report?.integritySummary?.missingFilesCount ?? null,
      missingZipEntries: report?.integritySummary?.missingZipEntriesCount ?? null,
      mismatchedArtifacts: report?.integritySummary?.mismatchedArtifactsCount ?? null,
    },
    publishReadiness: buildPublishReadinessExport(report?.publishReadiness),
    requiredArtifacts: requiredArtifactRows.map((artifact) => ({
      name: artifact.name,
      exists: artifact.exists === true,
      sizeBytes: artifact.sizeBytes,
      contentType: artifact.contentType,
    })),
    integrityFindings,
  }

  if (Object.keys(errors).length > 0) {
    value.errors = errors
  }

  return {
    filename: `package-review-${sequenceNumber || 'unknown'}-${jobId}.json`,
    value,
  }
}
