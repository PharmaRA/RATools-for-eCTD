import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { buildImportIssueColumns } from './importResultDisplay'

describe('importResultDisplay', () => {
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
