import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildIntegrityRiskSummaryItems,
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
    })).toBe('序列 0001 | Completed | FDA')
  })

  it('uses dashes for missing package review header fields', () => {
    expect(formatPackageReviewHeaderSummary(null)).toBe('序列 - | - | -')
    expect(formatPackageReviewHeaderSummary({
      sequenceNumber: '',
      publishJob: { status: '' },
      validationProfile: undefined,
    })).toBe('序列 - | - | -')
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
      { key: 'validation-errors', label: '校验错误', children: 0 },
      { key: 'warnings', label: '警告', children: 3 },
      { key: 'lifecycle-issues', label: '生命周期问题', children: 2 },
      { key: 'missing-files', label: '缺失文件', children: 1 },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: 0 },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: 4 },
    ])
  })

  it('uses dashes for package review risk summary counts when the report is unavailable', () => {
    expect(buildPackageReviewRiskSummaryItems({
      reportLoaded: false,
      lifecycleIssueCount: 2,
      report: null,
    })).toEqual([
      { key: 'validation-errors', label: '校验错误', children: '-' },
      { key: 'warnings', label: '警告', children: '-' },
      { key: 'lifecycle-issues', label: '生命周期问题', children: '-' },
      { key: 'missing-files', label: '缺失文件', children: '-' },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: '-' },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: '-' },
    ])
  })

  it('builds package review integrity risk summary items', () => {
    expect(buildPackageReviewIntegrityRiskSummaryItems({
      missingFilesCount: 1,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    })).toEqual([
      { key: 'missing-files', label: '缺失文件', children: 1 },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: 0 },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: '-' },
    ])
  })

  it('builds shared integrity risk summary items', () => {
    expect(buildIntegrityRiskSummaryItems({
      missingFilesCount: 2,
      missingZipEntriesCount: null,
      mismatchedArtifactsCount: 0,
    })).toEqual([
      { key: 'missing-files', label: '缺失文件', children: 2 },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: '-' },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: 0 },
    ])
  })

  it('formats package review warning alert descriptions when warnings remain', () => {
    expect(formatPackageReviewWarningAlertDescription(null)).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 0 })).toBeNull()
    expect(formatPackageReviewWarningAlertDescription({ warningCount: 2 })).toBe('仍有 2 个警告需要审阅人关注。')
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
    [true, { title: '可提交', iconClassName: 'text-green-500' }],
    [false, { title: '不可提交', iconClassName: 'text-red-500' }],
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
      { title: '检查项', dataIndex: 'check', key: 'check', width: undefined },
      { title: '状态', dataIndex: 'pass', key: 'status', width: 120 },
      { title: 'Details', dataIndex: 'detail', key: 'detail', width: undefined },
    ])

    const statusElement = (columns[1] as { render: (value: boolean) => unknown }).render(true)
    expect(isValidElement(statusElement)).toBe(true)
    expect((statusElement as ReactElement<{ color: string; children: string }>).props.color).toBe('green')
    expect((statusElement as ReactElement<{ color: string; children: string }>).props.children).toBe('通过')
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
      { title: '严重级别', dataIndex: 'severity', key: 'severity', width: 120 },
      { title: '代码', dataIndex: 'code', key: 'code', width: 220 },
      { title: '类别', dataIndex: 'category', key: 'category', width: 180 },
      { title: '字段', dataIndex: 'fieldName', key: 'fieldName', width: 180 },
      { title: '建议措施', dataIndex: 'recommendedAction', key: 'recommendedAction', width: undefined },
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
      { title: '严重级别', dataIndex: 'severity', key: 'severity', width: 100 },
      { title: '类型', dataIndex: 'type', key: 'type', width: 180 },
      { title: '路径', dataIndex: 'path', key: 'path', width: 260 },
      { title: '消息', dataIndex: 'message', key: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')

    expect((columns[2] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it('builds package review required artifact columns', () => {
    const columns = buildPackageReviewRequiredArtifactColumns()

    expect(columns.map(({ title, dataIndex, key }) => ({ title, dataIndex, key }))).toEqual([
      { title: '名称', dataIndex: 'name', key: 'name' },
      { title: '状态', dataIndex: 'exists', key: 'status' },
      { title: '大小', dataIndex: 'sizeBytes', key: 'size' },
      { title: '类型', dataIndex: 'contentType', key: 'type' },
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
