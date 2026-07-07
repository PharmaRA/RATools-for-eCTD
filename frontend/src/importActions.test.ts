import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError } from './apiClient'
import {
  buildImportApplicationUrl,
  importApplication,
  isLifecycleTargetImportIssue,
  mapImportErrorToMessage,
  normalizeImportIssueSeverity,
  summarizeImportIssues,
} from './importActions'

describe('importActions', () => {
  it('builds application import endpoint URL', () => {
    expect(buildImportApplicationUrl()).toBe('/api/applications/import')
  })

  it('summarizes import issues by lifecycle target category and severity', () => {
    const lifecycleMissingIssue = { severity: 'Warning', code: 'LIFECYCLE_TARGET_MISSING', sequenceNumber: '0002', message: 'Missing target' }
    const lifecycleNotImportedIssue = { severity: ' warning ', code: 'LIFECYCLE_TARGET_NOT_IMPORTED', sequenceNumber: '0003', message: 'Not imported' }
    const otherIssue = { severity: 'Error', code: 'SEQUENCE_INDEX_MISSING', sequenceNumber: '0004', message: 'Missing index' }

    const summary = summarizeImportIssues([lifecycleMissingIssue, lifecycleNotImportedIssue, otherIssue])

    expect(summary.lifecycleIssues).toEqual([lifecycleMissingIssue, lifecycleNotImportedIssue])
    expect(summary.otherIssues).toEqual([otherIssue])
    expect(summary.warningCount).toBe(2)
    expect(summary.errorCount).toBe(1)
  })

  it('classifies import issue severity and lifecycle target issues', () => {
    expect(normalizeImportIssueSeverity(' warning ')).toBe('warning')
    expect(normalizeImportIssueSeverity('Error')).toBe('error')
    expect(normalizeImportIssueSeverity('Info')).toBe('info')
    expect(isLifecycleTargetImportIssue({
      severity: 'Warning',
      code: 'LIFECYCLE_TARGET_NOT_IMPORTED',
      sequenceNumber: '0003',
      message: 'Not imported',
    })).toBe(true)
    expect(isLifecycleTargetImportIssue({
      severity: 'Error',
      code: 'SEQUENCE_INDEX_MISSING',
      sequenceNumber: '0004',
      message: 'Missing index',
    })).toBe(false)
  })

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
      ectdTemplateKey: 'us-fda-ectd-3-2-2',
      sponsorName: 'Demo Sponsor',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        workingDirectoryPath: 'D:\\work\\IND-IMPORT',
        ectdTemplateKey: 'us-fda-ectd-3-2-2',
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
