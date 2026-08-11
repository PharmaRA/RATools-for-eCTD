import { apiFetch, buildJsonRequestInit } from './apiClient'
import type { CreateSequenceContract } from './api/contracts'
import { buildApplicationUrl } from './applicationActions'

export type CreateSequenceRequest = CreateSequenceContract & {
  applicationId: string
}

export const buildSequencesUrl = (applicationId: string) => `${buildApplicationUrl(applicationId)}/sequences`

export const buildSequenceUrl = (applicationId: string, sequenceNumber: string) => {
  return `${buildSequencesUrl(applicationId)}/${sequenceNumber}`
}

export const createSequence = async (
  request: CreateSequenceRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  return executeRequest(
    buildSequencesUrl(request.applicationId),
    buildJsonRequestInit('POST', {
      sequenceNumber: request.sequenceNumber,
      submissionType: request.submissionType,
      description: request.description,
    }),
  )
}
