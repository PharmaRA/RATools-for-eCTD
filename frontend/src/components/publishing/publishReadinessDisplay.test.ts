import { describe, expect, it } from 'vitest'

import { formatOptionalText, getErrorSeverityTagColor } from '../../pages/appShared'
import {
  buildPublishReadinessCategoryColumns,
  buildPublishReadinessFindingColumns,
  buildPublishReadinessSnapshotItems,
  formatMissingMetadataFields,
  formatReadinessBlockingErrorCountHint,
  formatReadinessFieldName,
  formatReadinessHistoryCountHint,
  formatReadinessMissingMetadataHint,
  formatReadinessOptionalText,
  formatReadinessWarningCountHint,
  formatReadinessCount,
  formatReadinessReadyStatus,
  formatReadinessStatus,
  getPublishReadinessStatusTagProps,
  getPublishReadinessFindingSeverityTagColor,
  getPublishReadinessCategoryKey,
  getPublishReadinessFindingKey,
  getPublishReadinessFromReport,
  getPublishReadinessCategorySummaries,
  getPublishReadinessFindings,
} from './publishReadinessDisplay'

describe('publishReadinessDisplay', () => {
  it('formats missing metadata fields as a comma-separated list', () => {
    expect(formatMissingMetadataFields(['Applicant', 'Submission Type'])).toBe('Applicant, Submission Type')
  })

  it('uses None when no metadata fields are missing', () => {
    expect(formatMissingMetadataFields([])).toBe('None')
    expect(formatMissingMetadataFields(undefined)).toBe('None')
  })

  it('formats the primary missing metadata hint for history rows', () => {
    expect(formatReadinessMissingMetadataHint(['Applicant'])).toBe('Applicant')
    expect(formatReadinessMissingMetadataHint(['Applicant', 'Submission Type'])).toBe('Applicant +1')
    expect(formatReadinessMissingMetadataHint([])).toBeNull()
    expect(formatReadinessMissingMetadataHint(undefined)).toBeNull()
  })

  it('formats readiness history count hints after metadata hints are handled', () => {
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 2 }, null)).toBe('Warnings: 2')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, null)).toBe('Blocking errors: 3')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, 'Applicant')).toBeNull()
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 0 }, null)).toBeNull()
  })

  it('formats readiness warning count hints only for ready rows with warnings', () => {
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: 2 })).toBe('Warnings: 2')
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: 0 })).toBeNull()
    expect(formatReadinessWarningCountHint({ isReady: false, warningCount: 2 })).toBeNull()
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: null })).toBeNull()
  })

  it('formats readiness blocking error count hints only for blocked rows without metadata hints', () => {
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 3 }, null)).toBe('Blocking errors: 3')
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 3 }, 'Applicant')).toBeNull()
    expect(formatReadinessBlockingErrorCountHint({ isReady: true, blockingErrorCount: 3 }, null)).toBeNull()
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 0 }, null)).toBeNull()
  })

  it('formats publish readiness ready status', () => {
    expect(formatReadinessReadyStatus(true)).toBe('Yes')
    expect(formatReadinessReadyStatus(false)).toBe('No')
    expect(formatReadinessReadyStatus(undefined)).toBe('No')
  })

  it('formats optional readiness text with a dash placeholder', () => {
    expect(formatReadinessOptionalText('Blocked')).toBe('Blocked')
    expect(formatReadinessOptionalText('')).toBe('-')
    expect(formatReadinessOptionalText(null)).toBe('-')
    expect(formatReadinessOptionalText(undefined)).toBe('-')
  })

  it('shares the optional text formatter for readiness text', () => {
    expect(formatReadinessOptionalText).toBe(formatOptionalText)
  })

  it('uses a dash when publish readiness status is missing', () => {
    expect(formatReadinessStatus('Blocked')).toBe('Blocked')
    expect(formatReadinessStatus('')).toBe('-')
    expect(formatReadinessStatus(undefined)).toBe('-')
  })

  it('builds history readiness status tag props with status fallbacks', () => {
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: 'Ready' })).toEqual({
      color: 'green',
      label: 'Ready',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: '' })).toEqual({
      color: 'green',
      label: 'Ready',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: 'Blocked' })).toEqual({
      color: 'red',
      label: 'Blocked',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: '' })).toEqual({
      color: 'red',
      label: 'Blocked',
    })
  })

  it.each([
    ['Error', 'red'],
    ['error', 'red'],
    ['Warning', 'gold'],
  ])('maps publish readiness finding severity %s to tag color', (severity, color) => {
    expect(getPublishReadinessFindingSeverityTagColor(severity)).toBe(color)
  })

  it('shares the error severity tag color for readiness finding severity', () => {
    expect(getPublishReadinessFindingSeverityTagColor('error')).toBe(getErrorSeverityTagColor('error'))
    expect(getPublishReadinessFindingSeverityTagColor('Warning')).toBe(getErrorSeverityTagColor('Warning'))
  })

  it('uses a dash when publish readiness finding field is missing', () => {
    expect(formatReadinessFieldName('Applicant')).toBe('Applicant')
    expect(formatReadinessFieldName(null)).toBe('-')
    expect(formatReadinessFieldName(undefined)).toBe('-')
  })

  it('uses a dash only when a publish readiness count is missing', () => {
    expect(formatReadinessCount(2)).toBe(2)
    expect(formatReadinessCount(0)).toBe(0)
    expect(formatReadinessCount(null)).toBe('-')
    expect(formatReadinessCount(undefined)).toBe('-')
  })

  it('builds publish readiness snapshot description items', () => {
    expect(buildPublishReadinessSnapshotItems({
      status: '',
      isReady: false,
      blockingErrorCount: 2,
      warningCount: null,
      missingMetadataFields: ['Applicant', 'Submission Type'],
    })).toEqual([
      { key: 'readiness-status', label: 'Status', children: '-' },
      { key: 'readiness-ready', label: 'Ready', children: 'No' },
      { key: 'readiness-blocking-errors', label: 'Blocking Errors', children: 2 },
      { key: 'readiness-warnings', label: 'Warnings', children: '-' },
      { key: 'readiness-missing-fields', label: 'Missing Metadata Fields', children: 'Applicant, Submission Type' },
    ])
  })

  it('can span missing metadata fields across description columns', () => {
    expect(buildPublishReadinessSnapshotItems({
      missingMetadataFields: [],
    }, { missingMetadataFieldsSpan: 2 })).toContainEqual({
      key: 'readiness-missing-fields',
      label: 'Missing Metadata Fields',
      children: 'None',
      span: 2,
    })
  })

  it('reads optional publish readiness category summaries and findings', () => {
    const categorySummaries = [{ category: 'Metadata' }]
    const findings = [{ code: 'MISSING_FIELD', fieldName: 'Applicant' }]

    expect(getPublishReadinessCategorySummaries({ categorySummaries })).toBe(categorySummaries)
    expect(getPublishReadinessCategorySummaries({})).toEqual([])
    expect(getPublishReadinessCategorySummaries(null)).toEqual([])
    expect(getPublishReadinessCategorySummaries(undefined)).toEqual([])

    expect(getPublishReadinessFindings({ findings })).toBe(findings)
    expect(getPublishReadinessFindings({})).toEqual([])
    expect(getPublishReadinessFindings(null)).toEqual([])
    expect(getPublishReadinessFindings(undefined)).toEqual([])
  })

  it('reads optional publish readiness from report data', () => {
    const readiness = { isReady: true, status: 'Ready' }

    expect(getPublishReadinessFromReport({ publishReadiness: readiness })).toBe(readiness)
    expect(getPublishReadinessFromReport({ publishReadiness: null })).toBeNull()
    expect(getPublishReadinessFromReport({})).toBeNull()
    expect(getPublishReadinessFromReport(null)).toBeNull()
    expect(getPublishReadinessFromReport(undefined)).toBeNull()
  })

  it('uses category as the publish readiness category row key', () => {
    expect(getPublishReadinessCategoryKey({ category: 'Metadata' })).toBe('Metadata')
  })

  it('builds publish readiness category summary columns', () => {
    expect(buildPublishReadinessCategoryColumns()).toEqual([
      { title: 'Category', dataIndex: 'category', key: 'category' },
      { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', key: 'blockingErrorCount', width: 140 },
      { title: 'Warnings', dataIndex: 'warningCount', key: 'warningCount', width: 120 },
      { title: 'Findings', dataIndex: 'findingCount', key: 'findingCount', width: 120 },
    ])
  })

  it('can build report publish readiness category summary columns', () => {
    expect(buildPublishReadinessCategoryColumns({ categoryWidth: 220, includeKeys: false })).toEqual([
      { title: 'Category', dataIndex: 'category', width: 220 },
      { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', width: 140 },
      { title: 'Warnings', dataIndex: 'warningCount', width: 120 },
      { title: 'Findings', dataIndex: 'findingCount', width: 120 },
    ])
  })

  it('can build report publish readiness finding columns', () => {
    const columns = buildPublishReadinessFindingColumns({
      severityRenderer: (value: string) => `Severity: ${value}`,
      includeKeys: false,
    })

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', width: 100 },
      { title: 'Code', dataIndex: 'code', width: 220 },
      { title: 'Category', dataIndex: 'category', width: 180 },
      { title: 'Field', dataIndex: 'fieldName', width: 180 },
      { title: 'Recommended Action', dataIndex: 'recommendedAction', width: undefined },
    ])

    expect((columns[0] as { render: (value: string) => unknown }).render('Error')).toBe('Severity: Error')
    expect((columns[3] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it('builds a stable publish readiness finding row key', () => {
    expect(getPublishReadinessFindingKey({ code: 'MISSING_FIELD', fieldName: 'Applicant' }, 2))
      .toBe('MISSING_FIELD-Applicant-2')
    expect(getPublishReadinessFindingKey({ code: 'GLOBAL' }, 0)).toBe('GLOBAL-none-0')
  })
})
