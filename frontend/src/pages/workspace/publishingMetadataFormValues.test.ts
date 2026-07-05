import { describe, expect, it } from 'vitest'

import { buildSequencePublishingMetadataUpdateRequest } from './publishingMetadataFormValues'

describe('buildSequencePublishingMetadataUpdateRequest', () => {
  it('trims metadata fields and nulls blank optional values', () => {
    const request = buildSequencePublishingMetadataUpdateRequest('app-1', '0001', {
      applicationType: '  ',
      submissionType: ' supplemental-application ',
      submissionSubtype: ' labeling ',
      sequenceDescription: ' Updated sequence ',
      applicantName: ' Acme Pharma ',
      formType: '',
      applicantContactName: ' Jane Regulatory ',
      applicantContactType: ' regulatory ',
      telephone: ' ',
      telephoneNumberType: ' office ',
      email: ' ',
    })

    expect(request).toEqual({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      applicationType: null,
      submissionType: 'supplemental-application',
      submissionSubtype: 'labeling',
      sequenceDescription: 'Updated sequence',
      applicantName: 'Acme Pharma',
      formType: null,
      applicantContactName: 'Jane Regulatory',
      applicantContactType: 'regulatory',
      telephone: null,
      telephoneNumberType: 'office',
      email: null,
    })
  })
})
