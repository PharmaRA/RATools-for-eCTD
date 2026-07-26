import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { buildLifecycleIssueCountItems } from '../../pages/appShared'
import {
  buildReportArtifactManifestColumns,
  buildReportArtifactSummaryItems,
  buildReportAuditSummaryItems,
  buildReportIntegrityFindingColumns,
  buildReportIntegrityIssueSummaryItems,
  buildReportIntegritySummaryItems,
  buildReportLifecycleIssueSummaryItems,
  buildReportLifecycleMatchColumns,
  buildReportLifecycleSummaryItems,
  buildReportOverviewItems,
  buildReportPublishReadinessCategoryColumns,
  buildReportPublishReadinessFindingColumns,
  buildReportSummaryItems,
  buildReportValidationIssueColumns,
  getReportIntegrityArtifacts,
  getReportIntegrityFindings,
  getReportValidationIssues,
  getReportOutcomeDisplayMeta,
  formatReportIntegrityState,
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

  it.each([
    [true, { title: '发布成功', iconClassName: 'text-green-500' }],
    [false, { title: '发布失败', iconClassName: 'text-red-500' }],
    [undefined, { title: '发布失败', iconClassName: 'text-red-500' }],
  ] as const)('builds report outcome display meta for %s', (succeeded, expected) => {
    expect(getReportOutcomeDisplayMeta(succeeded)).toEqual(expected)
  })

  it('reads validation issues from optional report data', () => {
    const issues = [{ code: 'MISSING_LEAF', severity: 'Error', message: 'Missing leaf' }]

    expect(getReportValidationIssues({ validationReport: { issues } })).toBe(issues)
    expect(getReportValidationIssues({ validationReport: {} })).toEqual([])
    expect(getReportValidationIssues({})).toEqual([])
    expect(getReportValidationIssues(null)).toEqual([])
    expect(getReportValidationIssues(undefined)).toEqual([])
  })

  it('reads integrity findings from optional report data', () => {
    const findings = [{ type: 'MissingFile', severity: 'Error', message: 'Missing file' }]

    expect(getReportIntegrityFindings({ integrityEvidence: { findings } })).toBe(findings)
    expect(getReportIntegrityFindings({ integrityEvidence: {} })).toEqual([])
    expect(getReportIntegrityFindings({})).toEqual([])
    expect(getReportIntegrityFindings(null)).toEqual([])
    expect(getReportIntegrityFindings(undefined)).toEqual([])
  })

  it('reads integrity artifacts from optional report data', () => {
    const artifacts = [{ role: 'Package', relativePath: 'index.xml', exists: true }]

    expect(getReportIntegrityArtifacts({ integrityEvidence: { artifacts } })).toBe(artifacts)
    expect(getReportIntegrityArtifacts({ integrityEvidence: {} })).toEqual([])
    expect(getReportIntegrityArtifacts({})).toEqual([])
    expect(getReportIntegrityArtifacts(null)).toEqual([])
    expect(getReportIntegrityArtifacts(undefined)).toEqual([])
  })

  it('formats report integrity state from the summary', () => {
    expect(formatReportIntegrityState({ isConsistent: true })).toBe('一致')
    expect(formatReportIntegrityState({ isConsistent: false })).toBe('不一致')
    expect(formatReportIntegrityState({})).toBe('不一致')
    expect(formatReportIntegrityState(null)).toBe('-')
    expect(formatReportIntegrityState(undefined)).toBe('-')
  })

  it('builds report overview items', () => {
    expect(buildReportOverviewItems({
      validationProfile: 'Strict',
      durationMs: 42,
      errorCount: 1,
      warningCount: 2,
    }, 3, '一致')).toEqual([
      { key: 'profile', label: '配置', children: 'Strict' },
      { key: 'duration', label: '耗时', children: '42 ms' },
      { key: 'errors', label: '错误', children: 1 },
      { key: 'warnings', label: '警告', children: 2 },
      { key: 'lifecycle-issues', label: '生命周期问题', children: 3 },
      { key: 'integrity', label: '完整性', children: '一致' },
    ])
  })

  it('builds report summary items from display definitions', () => {
    expect(buildReportSummaryItems(
      { fileCount: 3, packageSizeBytes: null },
      [
        { key: 'file-count', label: '文件数', children: (summary) => formatReportCount(summary.fileCount) },
        { key: 'package-size', label: '包大小', children: (summary) => formatReportCount(summary.packageSizeBytes) },
      ],
    )).toEqual([
      { key: 'file-count', label: '文件数', children: 3 },
      { key: 'package-size', label: '包大小', children: '-' },
    ])
  })

  it('builds report integrity summary items', () => {
    expect(buildReportIntegritySummaryItems({
      missingFilesCount: 1,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    }, '不一致')).toEqual([
      { key: 'consistent', label: '一致', children: '不一致' },
      { key: 'missing-files', label: '缺失文件', children: 1 },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: 0 },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: '-' },
    ])
  })

  it('builds report integrity issue summary items', () => {
    expect(buildReportIntegrityIssueSummaryItems({
      missingFilesCount: 1,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    })).toEqual([
      { key: 'missing-files', label: '缺失文件', children: 1 },
      { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: 0 },
      { key: 'mismatched-artifacts', label: '不匹配的产物', children: '-' },
    ])
  })

  it('builds report artifact summary items', () => {
    expect(buildReportArtifactSummaryItems({
      fileCount: 3,
      totalSizeBytes: 1536,
      packageSizeBytes: null,
    })).toEqual([
      { key: 'file-count', label: '文件数', children: 3 },
      { key: 'total-size', label: '总大小', children: '1.5 KB' },
      { key: 'package-size', label: '包大小', children: '-' },
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
      { key: 'publish-job-events', label: '发布任务事件数', children: 0 },
      { key: 'validation-events', label: '校验事件数', children: 2 },
      { key: 'latest-action', label: '最近操作', children: '-' },
      { key: 'latest-event', label: '最近事件', children: new Date(latestEvent).toLocaleString() },
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
      { key: 'matched', label: '已匹配', children: 4 },
      { key: 'issues', label: '问题', children: 9 },
      { key: 'replace-missing', label: '替换目标缺失', children: 1 },
      { key: 'delete-missing', label: '删除目标缺失', children: 2 },
      { key: 'append-missing', label: '追加目标缺失', children: 3 },
      { key: 'ambiguous', label: '存在歧义', children: 0 },
      { key: 'current-sequence', label: '当前序列', children: 5 },
      { key: 'warning-summary', label: '警告摘要', children: '-' },
    ])
  })

  it('builds report lifecycle issue summary items', () => {
    const summary = {
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: 3,
      ambiguousCount: 0,
      currentSequenceCount: 5,
      issueCount: 9,
    }

    expect(buildReportLifecycleIssueSummaryItems(summary)).toEqual([
      { key: 'issues', label: '问题', children: 9 },
      { key: 'replace-missing', label: '替换目标缺失', children: 1 },
      { key: 'delete-missing', label: '删除目标缺失', children: 2 },
      { key: 'append-missing', label: '追加目标缺失', children: 3 },
      { key: 'ambiguous', label: '存在歧义', children: 0 },
      { key: 'current-sequence', label: '当前序列', children: 5 },
    ])
  })

  it('builds report lifecycle issue summary items from shared lifecycle issue counts', () => {
    const summary = {
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 2,
      appendTargetNotFoundCount: 3,
      ambiguousCount: 4,
      currentSequenceCount: 5,
      issueCount: 15,
    }

    expect(buildReportLifecycleIssueSummaryItems(summary).slice(1)).toEqual(
      buildLifecycleIssueCountItems(summary).map(({ key, label, value }) => ({ key, label, children: value })),
    )
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
      { title: '严重级别', dataIndex: 'severity', width: 100 },
      { title: '代码', dataIndex: 'code', width: 200 },
      { title: '消息', dataIndex: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')
  })

  it('builds report integrity finding columns', () => {
    const columns = buildReportIntegrityFindingColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: '严重级别', dataIndex: 'severity', width: 100 },
      { title: '类型', dataIndex: 'type', width: 200 },
      { title: '路径', dataIndex: 'path', width: 260 },
      { title: '消息', dataIndex: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')

    expect((columns[2] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it('builds report artifact manifest columns', () => {
    const columns = buildReportArtifactManifestColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: '角色', dataIndex: 'role', width: 140 },
      { title: '相对路径', dataIndex: 'relativePath', width: 260 },
      { title: '存在', dataIndex: 'exists', width: 120 },
      { title: '大小', dataIndex: 'sizeBytes', width: 120 },
      { title: 'Zip 条目', dataIndex: 'zipEntryPresent', width: 150 },
      { title: '来源', dataIndex: 'source', width: 160 },
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
      { title: '操作类型', dataIndex: 'operation', width: 120 },
      { title: '序列', dataIndex: 'sequenceNumber', width: 100 },
      { title: 'CTD 章节', dataIndex: 'ctdSection', width: 120 },
      { title: '文档 ID', dataIndex: 'documentId', width: 180 },
      { title: '结果代码', dataIndex: 'resultCode', width: 240 },
      { title: '匹配策略', dataIndex: 'matchStrategy', width: 180 },
      { title: '尝试的策略', dataIndex: 'attemptedStrategies', width: 220 },
      { title: '历史匹配数', dataIndex: 'historicalMatchCount', width: 140 },
      { title: '历史序列', dataIndex: 'historicalSequenceNumbers', width: 180 },
      { title: '历史放置 ID', dataIndex: 'historicalPlacementIds', width: 240 },
      { title: '最终状态', dataIndex: 'historicalFinalState', width: 140 },
    ])

    expect((columns[6] as { render: (value?: string[] | null) => unknown }).render(['exact', 'fallback'])).toBe('exact, fallback')
    expect((columns[8] as { render: (value?: string[] | null) => unknown }).render([])).toBe('-')
    expect((columns[9] as { render: (value?: string[] | null) => unknown }).render(undefined)).toBe('-')
  })

  it('builds report publish readiness category columns', () => {
    const columns = buildReportPublishReadinessCategoryColumns()

    expect(columns).toEqual([
      { title: '类别', dataIndex: 'category', width: 220 },
      { title: '阻断性错误', dataIndex: 'blockingErrorCount', width: 140 },
      { title: '警告', dataIndex: 'warningCount', width: 120 },
      { title: '发现项', dataIndex: 'findingCount', width: 120 },
    ])
  })

  it('builds report publish readiness finding columns', () => {
    const columns = buildReportPublishReadinessFindingColumns()

    expect(columns.map(({ title, dataIndex, width }) => ({ title, dataIndex, width }))).toEqual([
      { title: '严重级别', dataIndex: 'severity', width: 100 },
      { title: '代码', dataIndex: 'code', width: 220 },
      { title: '类别', dataIndex: 'category', width: 180 },
      { title: '字段', dataIndex: 'fieldName', width: 180 },
      { title: '建议措施', dataIndex: 'recommendedAction', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Error')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('red')

    expect((columns[3] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })

  it.each([
    [true, 'green', '存在'],
    [false, 'red', 'Zip 中缺失'],
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
