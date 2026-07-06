import { describe, expect, it } from 'vitest'

import {
  buildPackageReviewChecklistRows,
  formatPackageReviewChecklistCountDetail,
  formatPackageReviewIntegrityDetail,
  formatPackageReviewPublishDetail,
  formatPackageReviewRequiredArtifactsDetail,
  isPackageReviewReadyForSubmission,
} from './packageReviewChecklist'

describe('packageReviewChecklist', () => {
  it('formats checklist count details when report data is loaded', () => {
    expect(formatPackageReviewChecklistCountDetail(true, 2, 'error')).toBe('2 error(s)')
    expect(formatPackageReviewChecklistCountDetail(true, 1, 'issue')).toBe('1 issue(s)')
  })

  it('formats required artifact details with error precedence', () => {
    expect(formatPackageReviewRequiredArtifactsDetail(null, 2, 3)).toBe('2/3 present')
    expect(formatPackageReviewRequiredArtifactsDetail(new Error('Artifacts unavailable'), 2, 3))
      .toBe('Artifacts unavailable')
  })

  it('formats publish details with report message and error fallbacks', () => {
    expect(formatPackageReviewPublishDetail('Published', new Error('Report unavailable'))).toBe('Published')
    expect(formatPackageReviewPublishDetail('', new Error('Report unavailable'))).toBe('Report unavailable')
    expect(formatPackageReviewPublishDetail(undefined, null)).toBe('Report unavailable.')
  })

  it('formats integrity details from consistency evidence', () => {
    expect(formatPackageReviewIntegrityDetail({ isConsistent: true })).toBe('Consistent')
    expect(formatPackageReviewIntegrityDetail({ isConsistent: false })).toBe('Inconsistent or unavailable')
    expect(formatPackageReviewIntegrityDetail(undefined)).toBe('Inconsistent or unavailable')
  })

  it('checks whether every package review checklist row passes', () => {
    expect(isPackageReviewReadyForSubmission([
      { key: 'publish-succeeded', check: 'Publish succeeded', pass: true, detail: 'Published' },
      { key: 'validation-errors', check: 'Validation errors', pass: true, detail: '0 error(s)' },
    ])).toBe(true)

    expect(isPackageReviewReadyForSubmission([
      { key: 'publish-succeeded', check: 'Publish succeeded', pass: true, detail: 'Published' },
      { key: 'validation-errors', check: 'Validation errors', pass: false, detail: '1 error(s)' },
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
      { key: 'publish-succeeded', check: 'Publish succeeded', pass: true, detail: 'Published' },
      { key: 'validation-errors', check: 'Validation errors', pass: true, detail: '0 error(s)' },
      { key: 'lifecycle-issues', check: 'Lifecycle issues', pass: true, detail: '0 issue(s)' },
      { key: 'integrity-consistent', check: 'Integrity consistent', pass: true, detail: 'Consistent' },
      { key: 'required-artifacts-present', check: 'Required artifacts present', pass: true, detail: '3/3 present' },
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
      { key: 'publish-succeeded', check: 'Publish succeeded', pass: false, detail: 'Report unavailable' },
      { key: 'validation-errors', check: 'Validation errors', pass: false, detail: 'Unavailable' },
      { key: 'lifecycle-issues', check: 'Lifecycle issues', pass: false, detail: 'Unavailable' },
      { key: 'integrity-consistent', check: 'Integrity consistent', pass: false, detail: 'Inconsistent or unavailable' },
      { key: 'required-artifacts-present', check: 'Required artifacts present', pass: false, detail: 'Artifacts unavailable' },
    ])
  })
})
