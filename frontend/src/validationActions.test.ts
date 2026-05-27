import { describe, expect, it, vi } from 'vitest'

import { validateSequence } from './validationActions'

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
})
