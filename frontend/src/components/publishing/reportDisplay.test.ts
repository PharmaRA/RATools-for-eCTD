import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildReportArtifactManifestColumns,
  buildReportArtifactSummaryItems,
  buildReportAuditSummaryItems,
  buildReportIntegrityFindingColumns,
  buildReportIntegritySummaryItems,
  buildReportLifecycleMatchColumns,
  buildReportLifecycleSummaryItems,
  buildReportOverviewItems,
  buildReportPublishReadinessCategoryColumns,
  buildReportPublishReadinessFindingColumns,
  buildReportValidationIssueColumns,
  formatReportCount,
  formatReportList,
  renderReportSeverityStatus,
  renderZipEntryPresentStatus,
} from './reportDisplay'

describe('reportDisplay', () => {
  it('formats report list values as a comma-separated list', () => {
    expect(formatReportList(['Lifecycle', 'Validation'])).toBe('Lifecycle, Validation')
  })

  it('uses a dash when report list values are missing', () => {
    expect(formatReportList([])).toBe('-')
    expect(formatReportList(undefined)).toBe('-')
  })

  it('uses a dash only when a report count is missing', () => {
    expect(formatReportCount(3)).toBe(3)
    expect(formatReportCount(0)).toBe(0)
    expect(formatReportCount(null)).toBe('-')
    expect(formatReportCount(undefined)).toBe('-')
  })

  it('builds report overview items', () => {
    expect(buildReportOverviewItems({
      validationProfile: 'Strict',
      durationMs: 42,
      errorCount: 1,
      warningCount: 2,
    }, 3, 'Consistent')).toEqual([
      { key: 'profile', label: 'Profile', children: 'Strict' },
      { key: 'duration', label: 'Duration', children: '42 ms' },
      { key: 'errors', label: 'Errors', children: 1 },
      { key: 'warnings', label: 'Warnings', children: 2 },
      { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: 3 },
      { key: 'integrity', label: 'Integrity', children: 'Consistent' },
    ])
  })

  it('builds report integrity summary items', () => {
    expect(buildReportIntegritySummaryItems({
      missingFilesCount: 1,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    }, 'Inconsistent')).toEqual([
      { key: 'consistent', label: 'Consistent', children: 'Inconsistent' },
      { key: 'missing-files', label: 'Missing Files', children: 1 },
      { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: 0 },
      { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: '-' },
    ])
  })

  it('builds report artifact summary items', () => {
    expect(buildReportArtifactSummaryItems({
      fileCount: 3,
      totalSizeBytes: 1536,
      packageSizeBytes: null,
    })).toEqual([
      { key: 'file-count', label: 'File Count', children: 3 },
      { key: 'total-size', label: 'Total Size', children: '1.5 KB' },
      { key: 'package-size', label: 'Package Size', children: '-' },
    ])
  })

  it('builds report audit summary items', () => {
    const latestEvent = '2026-01-02T03:04:05Z'

    expect(buildReportAuditSummaryItems({
      publishJobEventCount: 0,
      validationEventCount: 2,
      latestPublishJobAction: null,
      latestPublishJobEventUtc: latestEvent,
    })).toEqual([
      { key: 'publish-job-events', label: 'Publish Job Events', children: 0 },
      { key: 'validation-events', label: 'Validation Events', children: 2 },
      { key: 'latest-action', label: 'Latest Action', children: '-' },
      { key: 'latest-event', label: 'Latest Event', children: new Date(latestEvent).toLocaleString() },
    ])
  })

  it('builds report lifecycle summary items', () => {
    expect(buildReportLifecycleSummaryItems({
      matchedCount: 4,
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: 3,
      ambiguousCount: 0,
      currentSequenceCount: 5,
      issueCount: 9,
    }, '')).toEqual([
      { key: 'matched', label: 'Matched', children: 4 },
      { key: 'issues', label: 'Issues', children: 9 },
      { key: 'replace-missing', label: 'Replace Missing', children: 1 },
      { key: 'delete-missing', label: 'Delete Missing', children: 2 },
      { key: 'append-missing', label: 'Append Missing', children: 3 },
      { key: 'ambiguous', label: 'Ambiguous', children: 0 },
      { key: 'current-sequence', label: 'Current Sequence', children: 5 },
      { key: 'warning-summary', label: 'Warning Summary', children: '-' },
    ])
  })

  it.each([
    ['Error', 'red'],
    ['Warning', 'orange'],
  ] as const)('renders %s report severity status', (severity, color) => {
    const element = renderReportSeverityStatus(severity)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(severity)
  })

  it('builds report validation issue columns', () => {
    const columns = buildReportValidationIssueColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', width: 100 },
      { title: 'Code', dataIndex: 'code', width: 200 },
      { title: 'Message', dataIndex: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')
  })

  it('builds report integrity finding columns', () => {
    const columns = buildReportIntegrityFindingColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', width: 100 },
      { title: 'Type', dataIndex: 'type', width: 200 },
      { title: 'Path', dataIndex: 'path', width: 260 },
      { title: 'Message', dataIndex: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')

    expect((columns[2] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it('builds report artifact manifest columns', () => {
    const columns = buildReportArtifactManifestColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Role', dataIndex: 'role', width: 140 },
      { title: 'Relative Path', dataIndex: 'relativePath', width: 260 },
      { title: 'Exists', dataIndex: 'exists', width: 120 },
      { title: 'Size', dataIndex: 'sizeBytes', width: 120 },
      { title: 'Zip Entry', dataIndex: 'zipEntryPresent', width: 150 },
      { title: 'Source', dataIndex: 'source', width: 160 },
    ])

    expect((columns[1] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')

    const existsElement = (columns[2] as { render: (value: boolean) => unknown }).render(true)
    expect(isValidElement(existsElement)).toBe(true)
    expect((existsElement as ReactElement<{ color: string; children: string }>).props.color).toBe('green')

    expect((columns[3] as { render: (value?: number | null) => unknown }).render(1536)).toBe('1.5 KB')

    const zipElement = (columns[4] as { render: (value: boolean) => unknown }).render(false)
    expect(isValidElement(zipElement)).toBe(true)
    expect((zipElement as ReactElement<{ color: string; children: string }>).props.color).toBe('red')
  })

  it('builds report lifecycle match columns', () => {
    const columns = buildReportLifecycleMatchColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Operation', dataIndex: 'operation', width: 120 },
      { title: 'Sequence', dataIndex: 'sequenceNumber', width: 100 },
      { title: 'CTD Section', dataIndex: 'ctdSection', width: 120 },
      { title: 'Document ID', dataIndex: 'documentId', width: 180 },
      { title: 'Result Code', dataIndex: 'resultCode', width: 240 },
      { title: 'Match Strategy', dataIndex: 'matchStrategy', width: 180 },
      { title: 'Attempted Strategies', dataIndex: 'attemptedStrategies', width: 220 },
      { title: 'Historical Matches', dataIndex: 'historicalMatchCount', width: 140 },
      { title: 'Historical Sequences', dataIndex: 'historicalSequenceNumbers', width: 180 },
      { title: 'Historical Placement IDs', dataIndex: 'historicalPlacementIds', width: 240 },
      { title: 'Final State', dataIndex: 'historicalFinalState', width: 140 },
    ])

    expect((columns[6] as { render: (value?: string[] | null) => unknown }).render(['exact', 'fallback'])).toBe('exact, fallback')
    expect((columns[8] as { render: (value?: string[] | null) => unknown }).render([])).toBe('-')
    expect((columns[9] as { render: (value?: string[] | null) => unknown }).render(undefined)).toBe('-')
  })

  it('builds report publish readiness category columns', () => {
    const columns = buildReportPublishReadinessCategoryColumns()

    expect(columns).toEqual([
      { title: 'Category', dataIndex: 'category', width: 220 },
      { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', width: 140 },
      { title: 'Warnings', dataIndex: 'warningCount', width: 120 },
      { title: 'Findings', dataIndex: 'findingCount', width: 120 },
    ])
  })

  it('builds report publish readiness finding columns', () => {
    const columns = buildReportPublishReadinessFindingColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', width: 100 },
      { title: 'Code', dataIndex: 'code', width: 220 },
      { title: 'Category', dataIndex: 'category', width: 180 },
      { title: 'Field', dataIndex: 'fieldName', width: 180 },
      { title: 'Recommended Action', dataIndex: 'recommendedAction', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Error')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('red')

    expect((columns[3] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it.each([
    [true, 'green', 'Present'],
    [false, 'red', 'Missing from zip'],
  ] as const)('renders zip entry present status %s', (present, color, label) => {
    const element = renderZipEntryPresentStatus(present)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(label)
  })

  it('uses a dash when zip entry present status is missing', () => {
    expect(renderZipEntryPresentStatus(null)).toBe('-')
    expect(renderZipEntryPresentStatus(undefined)).toBe('-')
  })
})
