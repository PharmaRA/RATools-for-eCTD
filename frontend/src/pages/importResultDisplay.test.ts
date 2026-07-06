import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildImportIssueColumns,
  buildImportIssueSummaryItems,
  getImportLifecycleWarningSummaryColor,
  getImportIssueSeverityDisplayMeta,
  getImportResultIssues,
} from './importResultDisplay'

describe('importResultDisplay', () => {
  it.each([
    ['Error', { alertType: 'error', tagColor: 'red' }],
    ['Warning', { alertType: 'warning', tagColor: 'gold' }],
    ['Info', { alertType: 'warning', tagColor: 'gold' }],
  ])('maps import issue severity %s to display meta', (severity, expected) => {
    expect(getImportIssueSeverityDisplayMeta(severity)).toEqual(expected)
  })

  it('builds import issue summary items', () => {
    expect(buildImportIssueSummaryItems({
      totalIssueCount: 3,
      warningCount: 2,
      errorCount: 1,
      lifecycleWarningCount: 0,
    })).toEqual([
      { key: 'total', color: 'blue', label: '3 total issues' },
      { key: 'warnings', color: 'gold', label: '2 warnings' },
      { key: 'errors', color: 'red', label: '1 errors' },
      { key: 'lifecycle-target-warnings', color: 'green', label: '0 lifecycle target warnings' },
    ])

    expect(buildImportIssueSummaryItems({
      totalIssueCount: 1,
      warningCount: 1,
      errorCount: 0,
      lifecycleWarningCount: 1,
    })[3].color).toBe('gold')
  })

  it.each([
    [0, 'green'],
    [1, 'gold'],
    [3, 'gold'],
  ] as const)('maps %s lifecycle warnings to summary color %s', (warningCount, expectedColor) => {
    expect(getImportLifecycleWarningSummaryColor(warningCount)).toBe(expectedColor)
  })

  it('reads import issues from optional import result data', () => {
    const issues = [{ severity: 'Warning', code: 'LIFECYCLE_TARGET', message: 'Review target' }]

    expect(getImportResultIssues({ issues })).toBe(issues)
    expect(getImportResultIssues({})).toEqual([])
    expect(getImportResultIssues(null)).toEqual([])
    expect(getImportResultIssues(undefined)).toEqual([])
  })

  it('builds import issue columns', () => {
    const columns = buildImportIssueColumns()

    expect(columns.map(({ title, dataIndex, key, width }) => ({ title, dataIndex, key, width }))).toEqual([
      { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 110 },
      { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
      { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130 },
      { title: 'Message', dataIndex: 'message', key: 'message', width: undefined },
    ])

    const severityElement = (columns[0] as { render: (value: string) => unknown }).render('Error')
    expect(isValidElement(severityElement)).toBe(true)
    expect((severityElement as ReactElement<{ color: string; children: string }>).props.color).toBe('red')

    const warningElement = (columns[0] as { render: (value: string) => unknown }).render('Warning')
    expect(isValidElement(warningElement)).toBe(true)
    expect((warningElement as ReactElement<{ color: string; children: string }>).props.color).toBe('gold')

    expect((columns[2] as { render: (value?: string | null) => unknown }).render(null)).toBe('-')
  })
})
