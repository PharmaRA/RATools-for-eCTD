import { apiFetch, buildJsonRequestInit } from './apiClient'

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

export const getSequencePublishingMetadata = async (
  request: SequencePublishingMetadataRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<SequencePublishingMetadata> => {
  return executeRequest(`/api/applications/${request.applicationId}/sequences/${request.sequenceNumber}/publishing-metadata`)
}

export const updateSequencePublishingMetadata = async (
  request: UpdateSequencePublishingMetadataRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<SequencePublishingMetadata> => {
  return executeRequest(
    `/api/applications/${request.applicationId}/sequences/${request.sequenceNumber}/publishing-metadata`,
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
