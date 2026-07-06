import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildPackageReviewChecklistColumns,
  buildPackageReviewEvidenceFindingColumns,
  buildPackageReviewIntegrityRiskSummaryItems,
  buildPackageReviewReadinessFindingColumns,
  buildPackageReviewRequiredArtifactColumns,
  buildPackageReviewRiskSummaryItems,
  formatPackageReviewHeaderSummary,
  formatPackageReviewWarningAlertDescription,
  getPackageReviewIntegrityFindings,
  getPackageReviewReadinessDisplayMeta,
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

  it('builds package review integrity risk summary items', () => {
    expect(buildPackageReviewIntegrityRiskSummaryItems({
      missingFilesCount: 1,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    })).toEqual([
      { key: 'missing-files', label: 'Missing Files', children: 1 },
      { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: 0 },
      { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: '-' },
    ])
  })

  it('formats package review warning alert descriptions when warnings remain', () => {
    expect(formatPackageReviewWarningAlertDescription(null)).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 0 })).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 2 })).toBe('2 warning(s) remain for reviewer awareness.')
  })

  it('reads package review integrity findings only after report load', () => {
    const findings = [{ type: 'MissingFile', severity: 'Error', message: 'Missing file' }]
    const report = { integrityEvidence: { findings } }

    expect(getPackageReviewIntegrityFindings(report, true)).toBe(findings)
    expect(getPackageReviewIntegrityFindings(report, false)).toEqual([])
    expect(getPackageReviewIntegrityFindings({ integrityEvidence: {} }, true)).toEqual([])
    expect(getPackageReviewIntegrityFindings(null, true)).toEqual([])
    expect(getPackageReviewIntegrityFindings(undefined, true)).toEqual([])
  })

  it.each([
    [true, { title: 'Ready for Submission', iconClassName: 'text-green-500' }],
    [false, { title: 'Not Ready for Submission', iconClassName: 'text-red-500' }],
  ] as const)('builds package review readiness display meta for %s', (readyForSubmission, expected) => {
    expect(getPackageReviewReadinessDisplayMeta(readyForSubmission)).toEqual(expected)
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

  it('builds package review checklist columns', () => {
    const columns = buildPackageReviewChecklistColumns()

    expect(columns.map(({ title, dataIndex, key, width }) => ({ title, dataIndex, key, width }))).toEqual([
      { title: 'Check', dataIndex: 'check', key: 'check', width: undefined },
      { title: 'Status', dataIndex: 'pass', key: 'status', width: 120 },
      { title: 'Details', dataIndex: 'detail', key: 'detail', width: undefined },
    ])

    const statusElement = (columns[1] as { render: (value: boolean) => unknown }).render(true)
    expect(isValidElement(statusElement)).toBe(true)
    expect((statusElement as ReactElement<{ color: string; children: string }>).props.color).toBe('green')
    expect((statusElement as ReactElement<{ color: string; children: string }>).props.children).toBe('Pass')
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

  it('builds package review readiness finding columns', () => {
    const columns = buildPackageReviewReadinessFindingColumns()

    expect(columns.map(({ title, dataIndex, key, width }) => ({ title, dataIndex, key, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 120 },
      { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
      { title: 'Category', dataIndex: 'category', key: 'category', width: 180 },
      { title: 'Field', dataIndex: 'fieldName', key: 'fieldName', width: 180 },
      { title: 'Recommended Action', dataIndex: 'recommendedAction', key: 'recommendedAction', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('gold')

    expect((columns[3] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
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

  it('builds package review evidence finding columns', () => {
    const columns = buildPackageReviewEvidenceFindingColumns()

    expect(columns.map(({ title, dataIndex, key, width }) => ({ title, dataIndex, key, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 100 },
      { title: 'Type', dataIndex: 'type', key: 'type', width: 180 },
      { title: 'Path', dataIndex: 'path', key: 'path', width: 260 },
      { title: 'Message', dataIndex: 'message', key: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')

    expect((columns[2] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it('builds package review required artifact columns', () => {
    const columns = buildPackageReviewRequiredArtifactColumns()

    expect(columns.map(({ title, dataIndex, key }) => ({ title, dataIndex, key }))).toEqual([
      { title: 'Name', dataIndex: 'name', key: 'name' },
      { title: 'Status', dataIndex: 'exists', key: 'status' },
      { title: 'Size', dataIndex: 'sizeBytes', key: 'size' },
      { title: 'Type', dataIndex: 'contentType', key: 'type' },
    ])

    const nameElement = (columns[0] as { render: (value: string) => unknown }).render('PackageZip')
    expect(isValidElement(nameElement)).toBe(true)
    expect((nameElement as ReactElement<{ children: string }>).type).toBe('b')
    expect((nameElement as ReactElement<{ children: string }>).props.children).toBe('PackageZip')

    const statusElement = (columns[1] as { render: (value: boolean) => unknown }).render(true)
    expect(isValidElement(statusElement)).toBe(true)
    expect((statusElement as ReactElement<{ color: string; children: string }>).props.color).toBe('green')

    expect((columns[2] as { render: (value?: number | null) => unknown }).render(1536)).toBe('1.5 KB')
    expect((columns[3] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })
})
