import { apiFetch, buildJsonRequestInit } from './apiClient'
import { buildSequenceUrl } from './sequenceActions'

export type SequencePublishingMetadataRequest = {
  applicationId: string
  sequenceNumber: string
}

export type SequencePublishingMetadata = {
  applicationId: string
  sequenceNumber: string
  standardsProfile: string
  applicationType?: string | null
  submissionType: string
  submissionSubtype?: string | null
  sequenceDescription: string
  applicantName: string
  formType?: string | null
  applicantContactName?: string | null
  applicantContactType?: string | null
  telephone?: string | null
  telephoneNumberType?: string | null
  email?: string | null
}

export type UpdateSequencePublishingMetadataRequest = SequencePublishingMetadataRequest & {
  applicationType?: string | null
  submissionType: string
  submissionSubtype?: string | null
  sequenceDescription: string
  applicantName: string
  formType?: string | null
  applicantContactName?: string | null
  applicantContactType?: string | null
  telephone?: string | null
  telephoneNumberType?: string | null
  email?: string | null
}

export const buildSequencePublishingMetadataUrl = (applicationId: string, sequenceNumber: string) => {
  return `${buildSequenceUrl(applicationId, sequenceNumber)}/publishing-metadata`
}

export const getSequencePublishingMetadata = async (
  request: SequencePublishingMetadataRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<SequencePublishingMetadata> => {
  return executeRequest(buildSequencePublishingMetadataUrl(request.applicationId, request.sequenceNumber))
}

export const updateSequencePublishingMetadata = async (
  request: UpdateSequencePublishingMetadataRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<SequencePublishingMetadata> => {
  return executeRequest(
    buildSequencePublishingMetadataUrl(request.applicationId, request.sequenceNumber),
    buildJsonRequestInit('PUT', {
      applicationType: request.applicationType,
      submissionType: request.submissionType,
      submissionSubtype: request.submissionSubtype,
      sequenceDescription: request.sequenceDescription,
      applicantName: request.applicantName,
      formType: request.formType,
      applicantContactName: request.applicantContactName,
      applicantContactType: request.applicantContactType,
      telephone: request.telephone,
      telephoneNumberType: request.telephoneNumberType,
      email: request.email,
    }),
  )
}
