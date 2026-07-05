import type { UpdateSequencePublishingMetadataRequest } from '../../sequencePublishingMetadataActions'
import type { MetadataFormValues } from './PublishModal'

const trimText = (value: string | undefined) => String(value || '').trim()

const optionalText = (value: string | undefined) => trimText(value) || null

export const buildSequencePublishingMetadataUpdateRequest = (
  applicationId: string,
  sequenceNumber: string,
  values: MetadataFormValues,
): UpdateSequencePublishingMetadataRequest => ({
  applicationId,
  sequenceNumber,
  applicationType: optionalText(values.applicationType),
  submissionType: trimText(values.submissionType),
  submissionSubtype: optionalText(values.submissionSubtype),
  sequenceDescription: trimText(values.sequenceDescription),
  applicantName: trimText(values.applicantName),
  formType: optionalText(values.formType),
  applicantContactName: optionalText(values.applicantContactName),
  applicantContactType: optionalText(values.applicantContactType),
  telephone: optionalText(values.telephone),
  telephoneNumberType: optionalText(values.telephoneNumberType),
  email: optionalText(values.email),
})
