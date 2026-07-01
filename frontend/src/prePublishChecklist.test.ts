import { describe, expect, it } from 'vitest'

import {
  apiErrorCode,
  buildPrePublishChecklistSummary,
  normalizeValidationReport,
  validationApiProfile,
} from './prePublishChecklist'
import type { ValidationReport } from './validationActions'

const createReport = (overrides: Partial<ValidationReport> = {}): ValidationReport => ({
  applicationId: 'app-1',
  sequenceNumber: '0000',
  validationProfile: 'US FDA eCTD 3.2.2',
  isValid: true,
  issues: [],
  sectionMatches: [],
  lifecycleMatches: [],
  ...overrides,
})

describe('prePublishChecklist', () => {
  it('fails closed when a validation report has unusable structure', () => {
    const report = {
      applicationId: 'app-1',
      sequenceNumber: '0000',
      validationProfile: '',
      isValid: true,
      issues: [{ severity: '', code: 'BROKEN', message: 'Missing severity' }],
      sectionMatches: [],
      lifecycleMatches: [],
    } as ValidationReport

    const normalized = normalizeValidationReport(report)

    expect(normalized.validationProfile).toBe(validationApiProfile)
    expect(normalized.issues).toContainEqual({
      severity: 'Error',
      code: apiErrorCode,
      message: 'Validation service returned an unusable report.',
    })
  })

  it('blocks publish for validation errors and keeps warnings non-blocking', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      issues: [
        { severity: 'Error', code: 'ERR', message: 'Blocking' },
        { severity: 'Warning', code: 'WARN', message: 'Awareness' },
      ],
    }))

    expect(summary.canProceed).toBe(false)
    expect(summary.blockingIssueCount).toBe(1)
    expect(summary.warningCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'blocking-errors')).toMatchObject({
      status: 'fail',
      blocking: true,
    })
  })

  it('marks lifecycle target errors as blocking lifecycle rows', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      issues: [{ severity: 'Error', code: 'REPLACE_TARGET_NOT_FOUND', message: 'Missing target' }],
      lifecycleMatches: [{
        operation: 'Replace',
        sequenceNumber: '0000',
        ctdSection: '1.2',
        documentId: 'doc-1',
        resultCode: 'REPLACE_TARGET_NOT_FOUND',
        matchStrategy: 'by-file-name',
        attemptedStrategies: ['by-file-name'],
        historicalMatchCount: 0,
        historicalSequenceNumbers: [],
        historicalPlacementIds: [],
        historicalFinalState: 'Missing',
      }],
    }))

    expect(summary.lifecycleIssueCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'lifecycle-targets')).toMatchObject({
      status: 'fail',
      blocking: true,
    })
  })

  it('keeps non-standard section matches informational unless backed by blocking issues', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      sectionMatches: [{
        sectionPath: '1.2',
        matchedPrefix: '1',
        isValid: true,
        isStandard: false,
        reason: 'Non-standard but allowed',
      }],
    }))

    expect(summary.canProceed).toBe(true)
    expect(summary.nonStandardSectionCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'section-paths')).toMatchObject({
      status: 'info',
      blocking: false,
    })
  })
})
