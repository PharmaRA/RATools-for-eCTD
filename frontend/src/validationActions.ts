import { apiFetch, buildJsonRequestInit } from './apiClient'

export type ValidateSequenceRequest = {
  applicationId: string
  sequenceNumber: string
}

export type ValidationIssue = {
  severity: string
  code: string
  message: string
  sectionPath?: string | null
  documentId?: string | null
  placementId?: string | null
}

export type ValidationSectionMatch = {
  sectionPath: string
  isValid: boolean
  isStandard: boolean
  matchedPrefix: string | null
  reason: string | null
}

export type ValidationLifecycleMatch = {
  operation: string
  sequenceNumber: string
  ctdSection: string
  documentId: string
  resultCode: string
  matchStrategy: string
  attemptedStrategies: string[]
  historicalMatchCount: number
  historicalSequenceNumbers: string[]
  historicalPlacementIds: string[]
  historicalFinalState: string
}

export type ValidationReport = {
  applicationId: string
  sequenceNumber: string
  validationProfile: string
  isValid: boolean
  issues: ValidationIssue[]
  sectionMatches: ValidationSectionMatch[]
  lifecycleMatches: ValidationLifecycleMatch[]
}

export type PublishReadinessFinding = {
  source: string
  severity: string
  code: string
  message: string
  category: string
  recommendedAction: string
  fieldName?: string | null
  sectionPath?: string | null
  documentId?: string | null
  placementId?: string | null
}

export type PublishReadinessCategorySummary = {
  category: string
  blockingErrorCount: number
  warningCount: number
  findingCount: number
}

export type PublishReadinessReport = {
  applicationId: string
  sequenceNumber: string
  isReady: boolean
  status: string
  blockingErrorCount: number
  warningCount: number
  validationReport: ValidationReport
  missingMetadataFields: string[]
  categorySummaries: PublishReadinessCategorySummary[]
  findings: PublishReadinessFinding[]
}

export const validateSequence = async (
  request: ValidateSequenceRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ValidationReport> => {
  return executeRequest('/api/validation/sequence', buildJsonRequestInit('POST', request))
}

export const getPublishReadiness = async (
  request: ValidateSequenceRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<PublishReadinessReport> => {
  return executeRequest('/api/validation/publish-readiness', buildJsonRequestInit('POST', request))
}
