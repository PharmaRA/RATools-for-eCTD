import { apiFetch } from './apiClient'

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

export const validateSequence = async (
  request: ValidateSequenceRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ValidationReport> => {
  return executeRequest('/api/validation/sequence', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
}
