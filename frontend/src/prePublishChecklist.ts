import { summarizeLifecycleMatches } from './publishLifecycleSummary'
import type {
  PublishReadinessReport,
  ValidationIssue,
  ValidationLifecycleMatch,
  ValidationReport,
  ValidationSectionMatch,
} from './validationActions'

export type PrePublishChecklistRow = {
  key: string
  label: string
  status: 'pass' | 'fail' | 'info'
  detail: string
  blocking: boolean
}

export type NormalizedValidationReport = {
  validationProfile: string
  issues: ValidationIssue[]
  sectionMatches: ValidationSectionMatch[]
  lifecycleMatches: ValidationLifecycleMatch[]
}

export const validationApiProfile = 'Validation API'
export const apiErrorCode = 'API_ERROR'
const structurallyUnusableReportMessage = 'Validation service returned an unusable report.'
export const blockingSectionIssueCodes = new Set(['INVALID_SECTION_PATH', 'SECTION_MISSING'])

const stringEqualsIgnoreCase = (left: string | null | undefined, right: string) => {
  return String(left || '').trim().toLowerCase() === right.toLowerCase()
}

const isErrorIssue = (issue: ValidationIssue) => stringEqualsIgnoreCase(issue.severity, 'Error')

const hasUsableIssueSeverity = (issue: ValidationIssue) => typeof issue?.severity === 'string'
  && issue.severity.trim().length > 0

export const normalizeValidationReport = (validationResult: ValidationReport): NormalizedValidationReport => {
  const issues = Array.isArray(validationResult.issues) ? validationResult.issues : []
  const sectionMatches = Array.isArray(validationResult.sectionMatches) ? validationResult.sectionMatches : []
  const lifecycleMatches = Array.isArray(validationResult.lifecycleMatches) ? validationResult.lifecycleMatches : []
  const validationProfile = validationResult.validationProfile || validationApiProfile
  const isStructurallyUsable = Array.isArray(validationResult.issues)
    && Array.isArray(validationResult.sectionMatches)
    && Array.isArray(validationResult.lifecycleMatches)
    && issues.every(hasUsableIssueSeverity)

  if (isStructurallyUsable) {
    return { validationProfile, issues, sectionMatches, lifecycleMatches }
  }

  return {
    validationProfile,
    issues: [
      ...issues,
      { severity: 'Error', code: apiErrorCode, message: structurallyUnusableReportMessage },
    ],
    sectionMatches,
    lifecycleMatches,
  }
}

const isBlockingSectionIssue = (issue: ValidationIssue) => {
  const code = String(issue.code || '').trim().toUpperCase()
  return blockingSectionIssueCodes.has(code)
    || (Boolean(issue.sectionPath?.trim()) && code.includes('SECTION'))
}

export const summarizeValidationIssues = (issues: ValidationIssue[]) => {
  const blockingIssues: ValidationIssue[] = []
  const warningIssues: ValidationIssue[] = []
  let hasApiError = false

  for (const issue of issues) {
    if (isErrorIssue(issue)) {
      blockingIssues.push(issue)
      if (stringEqualsIgnoreCase(issue.code, apiErrorCode)) {
        hasApiError = true
      }
    } else {
      warningIssues.push(issue)
    }
  }

  return {
    blockingIssues,
    warningIssues,
    hasApiError,
  }
}

export const getPublishReadinessValidationIssues = (readiness: PublishReadinessReport) => {
  const issues: ValidationIssue[] = []

  for (const finding of readiness.findings) {
    if (finding.severity.toLowerCase() !== 'error') {
      continue
    }

    issues.push({
      severity: finding.severity,
      code: finding.code,
      message: `[Publish readiness] ${finding.message}`,
      sectionPath: finding.sectionPath,
      documentId: finding.documentId,
      placementId: finding.placementId,
    })
  }

  return issues
}

export const summarizeSectionMatches = (sectionMatches: ValidationSectionMatch[]) => {
  const sectionRows: ValidationSectionMatch[] = []
  let invalidSectionCount = 0
  let nonStandardSectionCount = 0

  for (const match of sectionMatches) {
    const isInvalid = !match.isValid
    const isNonStandard = match.isValid && !match.isStandard

    if (isInvalid) {
      invalidSectionCount += 1
    }

    if (isNonStandard) {
      nonStandardSectionCount += 1
    }

    if (isInvalid || isNonStandard) {
      sectionRows.push(match)
    }
  }

  return {
    invalidSectionCount,
    nonStandardSectionCount,
    sectionRows,
  }
}

export const buildPrePublishChecklistSummary = (validationResult: ValidationReport) => {
  const normalizedResult = normalizeValidationReport(validationResult)
  const issues = normalizedResult.issues
  const sectionMatches = normalizedResult.sectionMatches
  const lifecycleMatches = normalizedResult.lifecycleMatches
  const issueSummary = summarizeValidationIssues(issues)
  const blockingIssues = issueSummary.blockingIssues
  const warningIssues = issueSummary.warningIssues
  const hasApiError = issueSummary.hasApiError
  const sectionSummary = summarizeSectionMatches(sectionMatches)
  const invalidSectionCount = sectionSummary.invalidSectionCount
  const nonStandardSectionCount = sectionSummary.nonStandardSectionCount
  const lifecycleSummary = summarizeLifecycleMatches(lifecycleMatches)
  const lifecycleIssueCount = lifecycleSummary.issueCount
  const sectionRows = sectionSummary.sectionRows
  const canProceed = !hasApiError && blockingIssues.length === 0
  const hasBlockingLifecycleIssue = blockingIssues.some((issue) => lifecycleMatches.some((match) => issue.code === match.resultCode)
    || issue.code.startsWith('LIFECYCLE_')
    || issue.code.endsWith('_TARGET_NOT_FOUND'))
  const hasBlockingSectionIssue = blockingIssues.some(isBlockingSectionIssue)
  const checklistRows: PrePublishChecklistRow[] = [
    {
      key: 'api-reachable',
      label: 'Validation API reachable',
      status: hasApiError ? 'fail' : 'pass',
      detail: hasApiError ? 'Validation service did not return a usable report.' : 'Validation API returned a report.',
      blocking: true,
    },
    {
      key: 'blocking-errors',
      label: 'No blocking validation errors',
      status: blockingIssues.length === 0 ? 'pass' : 'fail',
      detail: `${blockingIssues.length} blocking error(s)`,
      blocking: true,
    },
    {
      key: 'lifecycle-targets',
      label: 'Lifecycle targets resolved',
      status: lifecycleIssueCount === 0 ? 'pass' : hasBlockingLifecycleIssue ? 'fail' : 'info',
      detail: lifecycleMatches.length === 0
        ? 'No lifecycle operations were checked.'
        : `${lifecycleIssueCount} lifecycle issue(s)`,
      blocking: lifecycleIssueCount > 0 && hasBlockingLifecycleIssue,
    },
    {
      key: 'section-paths',
      label: 'Section paths acceptable',
      status: hasBlockingSectionIssue
        ? 'fail'
        : invalidSectionCount > 0 || nonStandardSectionCount > 0
          ? 'info'
          : 'pass',
      detail: `${invalidSectionCount} invalid | ${nonStandardSectionCount} non-standard`,
      blocking: hasBlockingSectionIssue,
    },
    {
      key: 'warnings-reviewed',
      label: 'Warnings reviewed',
      status: warningIssues.length === 0 ? 'pass' : 'info',
      detail: `${warningIssues.length} warning(s) for reviewer awareness`,
      blocking: false,
    },
  ]

  return {
    severity: canProceed ? 'success' as const : 'error' as const,
    profile: normalizedResult.validationProfile,
    issueCount: issues.length,
    blockingIssueCount: blockingIssues.length,
    warningCount: warningIssues.length,
    hasApiError,
    canProceed,
    blockingIssues,
    warningIssues,
    lifecycleMatches,
    lifecycleIssueCount,
    sectionMatches,
    invalidSectionCount,
    nonStandardSectionCount,
    sectionRows,
    checklistRows,
  }
}

export type PrePublishChecklistSummary = ReturnType<typeof buildPrePublishChecklistSummary>
