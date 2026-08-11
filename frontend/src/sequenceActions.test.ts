import { describe, expect, it, vi } from 'vitest'

import { buildSequenceUrl, buildSequencesUrl, createSequence } from './sequenceActions'

describe('sequenceActions', () => {
  it('builds sequence endpoint URLs', () => {
    expect(buildSequencesUrl('app-1')).toBe('/api/applications/app-1/sequences')
    expect(buildSequenceUrl('app-1', '0001')).toBe('/api/applications/app-1/sequences/0001')
  })

  it('creates a sequence through the application sequence endpoint', async () => {
    const request = vi.fn().mockResolvedValue({ sequenceNumber: '0001' })

    await expect(createSequence({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      submissionType: 'Original Application',
      description: 'Initial submission',
    }, request)).resolves.toEqual({ sequenceNumber: '0001' })

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/sequences', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        sequenceNumber: '0001',
        submissionType: 'Original Application',
        description: 'Initial submission',
      }),
    })
  })
})
