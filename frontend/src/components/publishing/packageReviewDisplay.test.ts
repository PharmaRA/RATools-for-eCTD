import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildPackageReviewRiskSummaryItems,
  formatPackageReviewHeaderSummary,
  formatPackageReviewWarningAlertDescription,
  renderChecklistPassStatus,
  renderEvidenceFindingSeverityStatus,
  renderReadinessFindingSeverityStatus,
} from './packageReviewDisplay'

describe('packageReviewDisplay', () => {
  it('formats the package review header summary from report fields', () => {
    expect(formatPackageReviewHeaderSummary({
      sequenceNumber: '0001',
      publishJob: { status: 'Completed' },
      validationProfile: 'FDA',
    })).toBe('Sequence 0001 | Completed | FDA')
  })

  it('uses dashes for missing package review header fields', () => {
    expect(formatPackageReviewHeaderSummary(null)).toBe('Sequence - | - | -')
    expect(formatPackageReviewHeaderSummary({
      sequenceNumber: '',
      publishJob: { status: '' },
      validationProfile: undefined,
    })).toBe('Sequence - | - | -')
  })

  it('builds package review risk summary items from report counts', () => {
    expect(buildPackageReviewRiskSummaryItems({
      reportLoaded: true,
      lifecycleIssueCount: 2,
      report: {
        errorCount: 0,
        warningCount: 3,
        integritySummary: {
          missingFilesCount: 1,
          missingZipEntriesCount: 0,
          mismatchedArtifactsCount: 4,
        },
      },
    })).toEqual([
      { key: 'validation-errors', label: 'Validation Errors', children: 0 },
      { key: 'warnings', label: 'Warnings', children: 3 },
      { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: 2 },
      { key: 'missing-files', label: 'Missing Files', children: 1 },
      { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: 0 },
      { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: 4 },
    ])
  })

  it('uses dashes for package review risk summary counts when the report is unavailable', () => {
    expect(buildPackageReviewRiskSummaryItems({
      reportLoaded: false,
      lifecycleIssueCount: 2,
      report: null,
    })).toEqual([
      { key: 'validation-errors', label: 'Validation Errors', children: '-' },
      { key: 'warnings', label: 'Warnings', children: '-' },
      { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: '-' },
      { key: 'missing-files', label: 'Missing Files', children: '-' },
      { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: '-' },
      { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: '-' },
    ])
  })

  it('formats package review warning alert descriptions when warnings remain', () => {
    expect(formatPackageReviewWarningAlertDescription(null)).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 0 })).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 2 })).toBe('2 warning(s) remain for reviewer awareness.')
  })

  it.each([
    [true, 'green', 'Pass'],
    [false, 'red', 'Fail'],
  ] as const)('renders checklist pass status %s', (pass, color, label) => {
    const element = renderChecklistPassStatus(pass)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(label)
  })

  it.each([
    ['Error', 'red'],
    ['Warning', 'gold'],
  ] as const)('renders readiness finding severity %s', (severity, color) => {
    const element = renderReadinessFindingSeverityStatus(severity)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(severity)
  })

  it.each([
    ['Error', 'red'],
    ['Warning', 'orange'],
  ] as const)('renders evidence finding severity %s', (severity, color) => {
    const element = renderEvidenceFindingSeverityStatus(severity)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(severity)
  })
})
