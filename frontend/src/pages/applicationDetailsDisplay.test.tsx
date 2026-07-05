import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { buildSequenceColumns } from './applicationDetailsDisplay'

type ColumnMetadata = {
  title?: string
  dataIndex?: string
  key?: string
}

type ActionButtonProps = {
  disabled?: boolean
  loading?: boolean
  onClick: () => void
}

const getColumnMetadata = (column: unknown) => {
  const { title, dataIndex, key } = column as ColumnMetadata
  return { title, dataIndex, key }
}

const getActionButtons = (element: unknown) => (
  element as ReactElement<{ children: ReactElement<ActionButtonProps>[] }>
).props.children

describe('applicationDetailsDisplay', () => {
  it('builds sequence table columns', () => {
    const onOpenWorkspace = vi.fn()
    const onDeleteSequence = vi.fn()
    const columns = buildSequenceColumns({
      isBatchDeleteRunning: false,
      deletingSequenceNumbers: new Set<string>(),
      onOpenWorkspace,
      onDeleteSequence,
    })

    expect(columns.map(getColumnMetadata)).toEqual([
      { title: 'Sequence', dataIndex: 'sequenceNumber', key: undefined },
      { title: 'Submission Type', dataIndex: 'submissionType', key: undefined },
      { title: 'Description', dataIndex: 'description', key: undefined },
      { title: 'Actions', dataIndex: undefined, key: 'actions' },
    ])

    const sequenceElement = (columns[0] as { render: (value: string) => unknown }).render('0001')
    expect(isValidElement(sequenceElement)).toBe(true)
    expect((sequenceElement as ReactElement<{ children: string }>).props.children).toBe('0001')

    const actionsElement = (columns[3] as { render: (_: unknown, record: { sequenceNumber: string }) => unknown })
      .render(null, { sequenceNumber: '0001' })
    expect(isValidElement(actionsElement)).toBe(true)

    const actionButtons = getActionButtons(actionsElement)
    expect(actionButtons).toHaveLength(2)
    expect(actionButtons[0].props.disabled).toBe(false)
    actionButtons[0].props.onClick()
    expect(onOpenWorkspace).toHaveBeenCalledWith('0001')

    expect(actionButtons[1].props.loading).toBe(false)
    expect(actionButtons[1].props.disabled).toBe(false)
    actionButtons[1].props.onClick()
    expect(onDeleteSequence).toHaveBeenCalledWith('0001')
  })

  it('disables sequence actions while deletion is running', () => {
    const columns = buildSequenceColumns({
      isBatchDeleteRunning: true,
      deletingSequenceNumbers: new Set<string>(['0001']),
      onOpenWorkspace: vi.fn(),
      onDeleteSequence: vi.fn(),
    })

    const actionsElement = (columns[3] as { render: (_: unknown, record: { sequenceNumber: string }) => unknown })
      .render(null, { sequenceNumber: '0001' })
    const actionButtons = getActionButtons(actionsElement)

    expect(actionButtons[0].props.disabled).toBe(true)
    expect(actionButtons[1].props.loading).toBe(true)
    expect(actionButtons[1].props.disabled).toBe(true)
  })
})
