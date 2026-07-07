import { describe, expect, it } from 'vitest'

import {
  apiErrorCode,
  buildPrePublishChecklistSummary,
  buildPrePublishChecklistDisplay,
  getPublishReadinessValidationIssues,
  isBlockingLifecycleIssue,
  isBlockingSectionIssue,
  normalizeValidationReport,
  summarizeSectionMatches,
  summarizeValidationIssues,
  validationApiProfile,
} from './prePublishChecklist'
import type { PublishReadinessReport, ValidationReport } from './validationActions'

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

const createPublishReadiness = (overrides: Partial<PublishReadinessReport> = {}): PublishReadinessReport => ({
  applicationId: 'app-1',
  sequenceNumber: '0000',
  isReady: false,
  status: 'Blocked',
  blockingErrorCount: 1,
  warningCount: 1,
  validationReport: createReport(),
  missingMetadataFields: [],
  categorySummaries: [],
  findings: [],
  ...overrides,
})

describe('prePublishChecklist', () => {
  it('summarizes validation issues by severity and API availability together', () => {
    const apiIssue = { severity: ' Error ', code: 'api_error', message: 'Unavailable' }
    const blockingIssue = { severity: 'Error', code: 'BROKEN', message: 'Blocking' }
    const warningIssue = { severity: 'Warning', code: 'WARN', message: 'Awareness' }

    const summary = summarizeValidationIssues([apiIssue, blockingIssue, warningIssue])

    expect(summary.blockingIssues).toEqual([apiIssue, blockingIssue])
    expect(summary.warningIssues).toEqual([warningIssue])
    expect(summary.hasApiError).toBe(true)
  })

  it('summarizes section match counts and visible rows together', () => {
    const invalidMatch = {
      sectionPath: '1.2',
      matchedPrefix: null,
      isValid: false,
      isStandard: false,
      reason: 'Unknown section',
    }
    const nonStandardMatch = {
      sectionPath: '1.3',
      matchedPrefix: '1',
      isValid: true,
      isStandard: false,
      reason: 'Allowed custom section',
    }
    const standardMatch = {
      sectionPath: '1.4',
      matchedPrefix: '1',
      isValid: true,
      isStandard: true,
      reason: 'Standard section',
    }

    const summary = summarizeSectionMatches([invalidMatch, nonStandardMatch, standardMatch])

    expect(summary.invalidSectionCount).toBe(1)
    expect(summary.nonStandardSectionCount).toBe(1)
    expect(summary.sectionRows).toEqual([invalidMatch, nonStandardMatch])
  })

  it('maps blocking publish readiness findings into validation issues', () => {
    const issues = getPublishReadinessValidationIssues(createPublishReadiness({
      findings: [
        {
          source: 'publish-readiness',
          severity: 'Error',
          code: 'MISSING_LEAF_TITLE',
          message: 'Leaf title is required.',
          category: 'RegionalMetadata',
          recommendedAction: 'Add the leaf title.',
          fieldName: 'leafTitle',
          sectionPath: '1.2',
          documentId: 'doc-1',
          placementId: 'placement-1',
        },
        {
          source: 'publish-readiness',
          severity: 'Warning',
          code: 'REVIEW_HINT',
          message: 'Reviewer awareness.',
          category: 'RegionalMetadata',
          recommendedAction: 'Review before publishing.',
        },
      ],
    }))

    expect(issues).toEqual([
      {
        severity: 'Error',
        code: 'MISSING_LEAF_TITLE',
        message: '[Publish readiness] Leaf title is required.',
        sectionPath: '1.2',
        documentId: 'doc-1',
        placementId: 'placement-1',
      },
    ])
  })

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

  it('classifies blocking lifecycle and section issues', () => {
    const lifecycleMatches = [{
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
    }]

    expect(isBlockingLifecycleIssue(
      { severity: 'Error', code: 'REPLACE_TARGET_NOT_FOUND', message: 'Missing target' },
      lifecycleMatches,
    )).toBe(true)
    expect(isBlockingLifecycleIssue(
      { severity: 'Error', code: 'LIFECYCLE_TARGET_MISSING', message: 'Missing target' },
      [],
    )).toBe(true)
    expect(isBlockingLifecycleIssue(
      { severity: 'Error', code: 'BROKEN', message: 'Blocking' },
      lifecycleMatches,
    )).toBe(false)

    expect(isBlockingSectionIssue({ severity: 'Error', code: 'invalid_section_path', message: 'Bad section' })).toBe(true)
    expect(isBlockingSectionIssue({
      severity: 'Error',
      code: 'CUSTOM_SECTION_ERROR',
      message: 'Bad section',
      sectionPath: '1.2',
    })).toBe(true)
    expect(isBlockingSectionIssue({ severity: 'Error', code: 'BROKEN', message: 'Blocking' })).toBe(false)
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

  it('builds validation summary display text from checklist counts', () => {
    expect(buildPrePublishChecklistDisplay({
      canProceed: true,
      blockingIssueCount: 0,
      warningCount: 1,
    })).toEqual({
      statusText: 'Pre-publish checks passed',
      issueCountText: '0 blocking | 1 warning',
    })

    expect(buildPrePublishChecklistDisplay({
      canProceed: false,
      blockingIssueCount: 2,
      warningCount: 3,
    })).toEqual({
      statusText: 'Pre-publish checks failed',
      issueCountText: '2 blocking | 3 warnings',
    })
  })
})
