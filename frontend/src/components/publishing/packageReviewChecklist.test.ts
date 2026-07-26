import { describe, expect, it } from 'vitest'

import { messages } from '../../i18n/messages'
import {
  buildPackageReviewChecklistRow,
  buildPackageReviewChecklistRows,
  formatPackageReviewChecklistCountDetail,
  formatPackageReviewIntegrityDetail,
  formatPackageReviewPublishDetail,
  formatPackageReviewRequiredArtifactsDetail,
  isPackageReviewReadyForSubmission,
} from './packageReviewChecklist'

const m = messages.packageReview

describe('packageReviewChecklist', () => {
  it('builds package review checklist rows from row fields', () => {
    expect(buildPackageReviewChecklistRow(
      'publish-succeeded',
      m.checkPublishSucceeded,
      true,
      'Published',
    )).toEqual({
      key: 'publish-succeeded',
      check: m.checkPublishSucceeded,
      pass: true,
      detail: 'Published',
    })
  })

  it('formats checklist count details when report data is loaded', () => {
    expect(formatPackageReviewChecklistCountDetail(true, 2, 'error')).toBe(`2 ${m.errorCountLabel}`)
    expect(formatPackageReviewChecklistCountDetail(true, 1, 'issue')).toBe(`1 ${m.issueCountLabel}`)
  })

  it('formats required artifact details with error precedence', () => {
    expect(formatPackageReviewRequiredArtifactsDetail(null, 2, 3)).toBe(`2/3 ${m.artifactsReadySuffix}`)
    expect(formatPackageReviewRequiredArtifactsDetail(new Error('Artifacts unavailable'), 2, 3))
      .toBe('Artifacts unavailable')
  })

  it('formats publish details with report message and error fallbacks', () => {
    expect(formatPackageReviewPublishDetail('Published', new Error('Report unavailable'))).toBe('Published')
    expect(formatPackageReviewPublishDetail('', new Error('Report unavailable'))).toBe('Report unavailable')
    expect(formatPackageReviewPublishDetail(undefined, null)).toBe(m.reportUnavailable)
  })

  it('formats integrity details from consistency evidence', () => {
    expect(formatPackageReviewIntegrityDetail({ isConsistent: true })).toBe(m.integrityConsistent)
    expect(formatPackageReviewIntegrityDetail({ isConsistent: false })).toBe(m.integrityInconsistent)
    expect(formatPackageReviewIntegrityDetail(undefined)).toBe(m.integrityInconsistent)
  })

  it('checks whether every package review checklist row passes', () => {
    expect(isPackageReviewReadyForSubmission([
      { key: 'publish-succeeded', check: m.checkPublishSucceeded, pass: true, detail: 'Published' },
      { key: 'validation-errors', check: m.checkValidationErrors, pass: true, detail: `0 ${m.errorCountLabel}` },
    ])).toBe(true)

    expect(isPackageReviewReadyForSubmission([
      { key: 'publish-succeeded', check: m.checkPublishSucceeded, pass: true, detail: 'Published' },
      { key: 'validation-errors', check: m.checkValidationErrors, pass: false, detail: `1 ${m.errorCountLabel}` },
    ])).toBe(false)

    expect(isPackageReviewReadyForSubmission([])).toBe(true)
  })

  it('builds passing checklist rows when report and required artifacts are ready', () => {
    expect(buildPackageReviewChecklistRows({
      reportLoaded: true,
      reportError: null,
      artifactsError: null,
      lifecycleIssueCount: 0,
      presentArtifactCount: 3,
      requiredArtifactCount: 3,
      report: {
        succeeded: true,
        message: 'Published',
        errorCount: 0,
        integritySummary: { isConsistent: true },
      },
    })).toEqual([
      { key: 'publish-succeeded', check: m.checkPublishSucceeded, pass: true, detail: 'Published' },
      { key: 'validation-errors', check: m.checkValidationErrors, pass: true, detail: `0 ${m.errorCountLabel}` },
      { key: 'lifecycle-issues', check: m.checkLifecycleIssues, pass: true, detail: `0 ${m.issueCountLabel}` },
      { key: 'integrity-consistent', check: m.checkIntegrityConsistent, pass: true, detail: m.integrityConsistent },
      { key: 'required-artifacts-present', check: m.checkRequiredArtifacts, pass: true, detail: `3/3 ${m.artifactsReadySuffix}` },
    ])
  })

  it('builds failing checklist rows from missing report data and artifact errors', () => {
    expect(buildPackageReviewChecklistRows({
      reportLoaded: false,
      report: null,
      reportError: new Error('Report unavailable'),
      artifactsError: new Error('Artifacts unavailable'),
      lifecycleIssueCount: 2,
      presentArtifactCount: 1,
      requiredArtifactCount: 3,
    })).toEqual([
      { key: 'publish-succeeded', check: m.checkPublishSucceeded, pass: false, detail: 'Report unavailable' },
      { key: 'validation-errors', check: m.checkValidationErrors, pass: false, detail: messages.common.unavailable },
      { key: 'lifecycle-issues', check: m.checkLifecycleIssues, pass: false, detail: messages.common.unavailable },
      { key: 'integrity-consistent', check: m.checkIntegrityConsistent, pass: false, detail: m.integrityInconsistent },
      { key: 'required-artifacts-present', check: m.checkRequiredArtifacts, pass: false, detail: 'Artifacts unavailable' },
    ])
  })
})
