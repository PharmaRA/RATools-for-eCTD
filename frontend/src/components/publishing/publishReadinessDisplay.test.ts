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
    expect(formatMissingMetadataFields([])).toBe('无')
    expect(formatMissingMetadataFields(undefined)).toBe('无')
  })

  it('formats the primary missing metadata hint for history rows', () => {
    expect(formatReadinessMissingMetadataHint(['Applicant'])).toBe('Applicant')
    expect(formatReadinessMissingMetadataHint(['Applicant', 'Submission Type'])).toBe('Applicant +1')
    expect(formatReadinessMissingMetadataHint([])).toBeNull()
    expect(formatReadinessMissingMetadataHint(undefined)).toBeNull()
  })

  it('formats readiness history count hints after metadata hints are handled', () => {
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 2 }, null)).toBe('警告：2')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, null)).toBe('阻断性错误：3')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, 'Applicant')).toBeNull()
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 0 }, null)).toBeNull()
  })

  it('formats readiness warning count hints only for ready rows with warnings', () => {
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: 2 })).toBe('警告：2')
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: 0 })).toBeNull()
    expect(formatReadinessWarningCountHint({ isReady: false, warningCount: 2 })).toBeNull()
    expect(formatReadinessWarningCountHint({ isReady: true, warningCount: null })).toBeNull()
  })

  it('formats readiness blocking error count hints only for blocked rows without metadata hints', () => {
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 3 }, null)).toBe('阻断性错误：3')
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 3 }, 'Applicant')).toBeNull()
    expect(formatReadinessBlockingErrorCountHint({ isReady: true, blockingErrorCount: 3 }, null)).toBeNull()
    expect(formatReadinessBlockingErrorCountHint({ isReady: false, blockingErrorCount: 0 }, null)).toBeNull()
  })

  it('formats publish readiness ready status', () => {
    expect(formatReadinessReadyStatus(true)).toBe('是')
    expect(formatReadinessReadyStatus(false)).toBe('否')
    expect(formatReadinessReadyStatus(undefined)).toBe('否')
  })

  it('formats optional readiness text with a dash placeholder', () => {
    expect(formatReadinessOptionalText('受阻')).toBe('受阻')
    expect(formatReadinessOptionalText('')).toBe('-')
    expect(formatReadinessOptionalText(null)).toBe('-')
    expect(formatReadinessOptionalText(undefined)).toBe('-')
  })

  it('shares the optional text formatter for readiness text', () => {
    expect(formatReadinessOptionalText).toBe(formatOptionalText)
  })

  it('uses a dash when publish readiness status is missing', () => {
    expect(formatReadinessStatus('受阻')).toBe('受阻')
    expect(formatReadinessStatus('')).toBe('-')
    expect(formatReadinessStatus(undefined)).toBe('-')
  })

  it('builds history readiness status tag props with status fallbacks', () => {
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: '就绪' })).toEqual({
      color: 'green',
      label: '就绪',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: '' })).toEqual({
      color: 'green',
      label: '就绪',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: '受阻' })).toEqual({
      color: 'red',
      label: '受阻',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: '' })).toEqual({
      color: 'red',
      label: '受阻',
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
      { key: 'readiness-status', label: '状态', children: '-' },
      { key: 'readiness-ready', label: '就绪', children: '否' },
      { key: 'readiness-blocking-errors', label: '阻断性错误', children: 2 },
      { key: 'readiness-warnings', label: '警告', children: '-' },
      { key: 'readiness-missing-fields', label: '缺失的元数据字段', children: 'Applicant, Submission Type' },
    ])
  })

  it('can span missing metadata fields across description columns', () => {
    expect(buildPublishReadinessSnapshotItems({
      missingMetadataFields: [],
    }, { missingMetadataFieldsSpan: 2 })).toContainEqual({
      key: 'readiness-missing-fields',
      label: '缺失的元数据字段',
      children: '无',
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
    const readiness = { isReady: true, status: '就绪' }

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
      { title: '类别', dataIndex: 'category', key: 'category' },
      { title: '阻断性错误', dataIndex: 'blockingErrorCount', key: 'blockingErrorCount', width: 140 },
      { title: '警告', dataIndex: 'warningCount', key: 'warningCount', width: 120 },
      { title: '发现项', dataIndex: 'findingCount', key: 'findingCount', width: 120 },
    ])
  })

  it('can build report publish readiness category summary columns', () => {
    expect(buildPublishReadinessCategoryColumns({ categoryWidth: 220, includeKeys: false })).toEqual([
      { title: '类别', dataIndex: 'category', width: 220 },
      { title: '阻断性错误', dataIndex: 'blockingErrorCount', width: 140 },
      { title: '警告', dataIndex: 'warningCount', width: 120 },
      { title: '发现项', dataIndex: 'findingCount', width: 120 },
    ])
  })

  it('can build report publish readiness finding columns', () => {
    const columns = buildPublishReadinessFindingColumns({
      severityRenderer: (value: string) => `Severity: ${value}`,
      includeKeys: false,
    })

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: '严重级别', dataIndex: 'severity', width: 100 },
      { title: '代码', dataIndex: 'code', width: 220 },
      { title: '类别', dataIndex: 'category', width: 180 },
      { title: '字段', dataIndex: 'fieldName', width: 180 },
      { title: '建议措施', dataIndex: 'recommendedAction', width: undefined },
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
