import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError } from './apiClient'
import { importApplication, mapImportErrorToMessage } from './importActions'

describe('importActions', () => {
  it('submits import request and returns parsed result', async () => {
    const request = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      applicationNumber: 'IND-IMPORT',
      workingDirectoryPath: 'D:\\work\\IND-IMPORT',
      importedSequenceCount: 1,
      importedDocumentCount: 1,
      importedPlacementCount: 1,
      skippedSequenceCount: 0,
      failedSequenceCount: 0,
      issues: [],
    })

    const result = await importApplication({
      workingDirectoryPath: 'D:\\work\\IND-IMPORT',
      region: 'US',
      sponsorName: 'Demo Sponsor',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        workingDirectoryPath: 'D:\\work\\IND-IMPORT',
        region: 'US',
        sponsorName: 'Demo Sponsor',
      }),
    })
    expect(result.applicationNumber).toBe('IND-IMPORT')
  })

  it('maps 409 conflicts to user-friendly message', () => {
    const error = new ApiRequestError(409, 'Application already imported')
    expect(mapImportErrorToMessage(error)).toContain('导入冲突')
  })

  it('maps 400 bad request to backend message', () => {
    const error = new ApiRequestError(400, 'WORKING_DIRECTORY_NOT_FOUND: missing')
    expect(mapImportErrorToMessage(error)).toContain('WORKING_DIRECTORY_NOT_FOUND')
  })

  it('maps unknown errors to fallback message', () => {
    expect(mapImportErrorToMessage(new Error('network down'))).toContain('导入失败')
  })
})
