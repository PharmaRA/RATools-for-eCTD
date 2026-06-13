import { describe, expect, it, vi } from 'vitest'

import { getPublishReadiness, validateSequence } from './validationActions'

describe('validationActions', () => {
  it('validates a sequence and returns the backend validation report shape', async () => {
    const validationReport = {
      applicationId: '11111111-1111-1111-1111-111111111111',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [
        {
          severity: 'Error',
          code: 'INVALID_SECTION',
          message: 'Section is not valid.',
        },
      ],
      sectionMatches: [
        {
          sectionPath: 'm1/us/cover-letter',
          isValid: true,
          isStandard: true,
          matchedPrefix: 'm1/us',
          reason: null,
        },
      ],
      lifecycleMatches: [
        {
          operation: 'replace',
          sequenceNumber: '0001',
          ctdSection: 'm1/us/cover-letter',
          documentId: '22222222-2222-2222-2222-222222222222',
          resultCode: 'MATCHED',
          matchStrategy: 'DocumentId',
          attemptedStrategies: ['DocumentId'],
          historicalMatchCount: 1,
          historicalSequenceNumbers: ['0000'],
          historicalPlacementIds: ['33333333-3333-3333-3333-333333333333'],
          historicalFinalState: 'Current',
        },
      ],
    }
    const request = vi.fn().mockResolvedValue(validationReport)

    const result = await validateSequence({
      applicationId: '11111111-1111-1111-1111-111111111111',
      sequenceNumber: '0001',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/validation/sequence', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationId: '11111111-1111-1111-1111-111111111111',
        sequenceNumber: '0001',
      }),
    })
    expect(result).toEqual(validationReport)
  })

  it('loads publish readiness using the validation request payload', async () => {
    const readinessReport = {
      applicationId: '11111111-1111-1111-1111-111111111111',
      sequenceNumber: '0001',
      isReady: false,
      status: 'Blocked',
      blockingErrorCount: 1,
      warningCount: 0,
      validationReport: {
        applicationId: '11111111-1111-1111-1111-111111111111',
        sequenceNumber: '0001',
        validationProfile: 'US FDA eCTD 3.2.2',
        isValid: true,
        issues: [],
        sectionMatches: [],
        lifecycleMatches: [],
      },
      missingMetadataFields: ['ApplicantContactName'],
      categorySummaries: [
        {
          category: 'RegionalMetadata',
          blockingErrorCount: 1,
          warningCount: 0,
          findingCount: 1,
        },
      ],
      findings: [
        {
          source: 'PublishPreflight',
          severity: 'Error',
          code: 'US_REGIONAL_METADATA_MISSING',
          message: "metadata field 'ApplicantContactName' is required.",
          category: 'RegionalMetadata',
          recommendedAction: 'Populate the required US Regional publishing metadata field before publishing.',
          fieldName: 'ApplicantContactName',
          sectionPath: null,
          documentId: null,
          placementId: null,
        },
      ],
    }
    const request = vi.fn().mockResolvedValue(readinessReport)

    const result = await getPublishReadiness({
      applicationId: '11111111-1111-1111-1111-111111111111',
      sequenceNumber: '0001',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/validation/publish-readiness', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationId: '11111111-1111-1111-1111-111111111111',
        sequenceNumber: '0001',
      }),
    })
    expect(result).toEqual(readinessReport)
  })
})
