import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { buildEvidenceFindingColumns } from './evidenceFindingDisplay'

describe('evidenceFindingDisplay', () => {
  it('builds keyed evidence finding columns with configurable widths', () => {
    const columns = buildEvidenceFindingColumns({ typeWidth: 180 })

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

  it('omits evidence finding column keys when requested', () => {
    const columns = buildEvidenceFindingColumns({ includeKeys: false })

    expect(columns).toEqual([
      { title: 'Severity', dataIndex: 'severity', width: 100, render: expect.any(Function) },
      { title: 'Type', dataIndex: 'type', width: 200 },
      { title: 'Path', dataIndex: 'path', width: 260, render: expect.any(Function) },
      { title: 'Message', dataIndex: 'message' },
    ])
  })
})
