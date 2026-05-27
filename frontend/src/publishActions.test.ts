import { describe, expect, it, vi } from 'vitest'

import { createAndExecutePublishJob } from './publishActions'

describe('publishActions', () => {
  it('executes a publish job using only application, sequence, and output directory', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({ succeeded: true })

    await createAndExecutePublishJob({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      outputDirectoryPath: 'E:/exports/submission-a',
    }, request)

    expect(request).toHaveBeenCalledOnce()
    expect(request).toHaveBeenCalledWith('/api/publish-jobs/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationId: 'app-1',
        sequenceNumber: '0001',
        outputDirectoryPath: 'E:/exports/submission-a',
      }),
    })
  })
})
