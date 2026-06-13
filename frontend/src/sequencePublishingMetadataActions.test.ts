import { describe, expect, it, vi } from 'vitest'

import {
  getSequencePublishingMetadata,
  updateSequencePublishingMetadata,
} from './sequencePublishingMetadataActions'

describe('sequencePublishingMetadataActions', () => {
  it('loads sequence publishing metadata', async () => {
    const response = {
      applicationId: 'app-1',
      sequenceNumber: '0001',
      standardsProfile: 'FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3',
      applicationType: 'IND',
      submissionType: 'original-application',
      submissionSubtype: 'initial',
      sequenceDescription: 'Initial sequence',
      applicantName: 'Acme Pharma',
      formType: '356h',
      applicantContactName: 'Jane Regulatory',
      applicantContactType: 'regulatory',
      telephone: '301-555-0100',
      telephoneNumberType: 'office',
      email: 'jane.regulatory@example.test',
    }
    const request = vi.fn().mockResolvedValue(response)

    const result = await getSequencePublishingMetadata({
      applicationId: 'app-1',
      sequenceNumber: '0001',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/sequences/0001/publishing-metadata')
    expect(result).toEqual(response)
  })

  it('updates sequence publishing metadata using the API contract shape', async () => {
    const request = vi.fn().mockResolvedValue({ ok: true })

    await updateSequencePublishingMetadata({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      applicationType: 'IND',
      submissionType: 'supplemental-application',
      submissionSubtype: 'labeling',
      sequenceDescription: 'Updated sequence description',
      applicantName: 'Acme Pharma',
      formType: '356h',
      applicantContactName: 'Jane Regulatory',
      applicantContactType: 'regulatory',
      telephone: '301-555-0100',
      telephoneNumberType: 'office',
      email: 'jane.regulatory@example.test',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/sequences/0001/publishing-metadata', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationType: 'IND',
        submissionType: 'supplemental-application',
        submissionSubtype: 'labeling',
        sequenceDescription: 'Updated sequence description',
        applicantName: 'Acme Pharma',
        formType: '356h',
        applicantContactName: 'Jane Regulatory',
        applicantContactType: 'regulatory',
        telephone: '301-555-0100',
        telephoneNumberType: 'office',
        email: 'jane.regulatory@example.test',
      }),
    })
  })
})
