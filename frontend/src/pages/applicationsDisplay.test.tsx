import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { type Application } from './appShared'
import { buildApplicationColumns } from './applicationsDisplay'

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

const application: Application = {
  id: 'app-1',
  applicationNumber: 'NDA123456',
  ectdTemplateDisplayName: 'US FDA',
  sponsorName: 'Acme Pharma',
  createdUtc: '2025-01-01T00:00:00Z',
  sequences: [
    { sequenceNumber: '0001' },
    { sequenceNumber: '0002' },
  ],
}

const getColumnMetadata = (column: unknown) => {
  const { title, dataIndex, key } = column as ColumnMetadata
  return { title, dataIndex, key }
}

const getActionButtons = (element: unknown) => (
  element as ReactElement<{ children: ReactElement<ActionButtonProps>[] }>
).props.children

describe('applicationsDisplay', () => {
  it('builds application table columns', () => {
    const onSelectApp = vi.fn()
    const onDeleteApp = vi.fn()
    const columns = buildApplicationColumns({
      isBatchDeleteRunning: false,
      deletingAppIds: new Set<string>(),
      onSelectApp,
      onDeleteApp,
    })

    expect(columns.map(getColumnMetadata)).toEqual([
      { title: '申请编号', dataIndex: 'applicationNumber', key: undefined },
      { title: 'eCTD 模板', dataIndex: undefined, key: 'ectdTemplate' },
      { title: '申办方', dataIndex: 'sponsorName', key: undefined },
      { title: '创建时间', dataIndex: 'createdUtc', key: undefined },
      { title: '序列数', dataIndex: undefined, key: 'sequences' },
      { title: '操作', dataIndex: undefined, key: 'action' },
    ])

    const appNumberElement = (columns[0] as { render: (value: string) => unknown }).render('NDA123456')
    expect(isValidElement(appNumberElement)).toBe(true)
    expect((appNumberElement as ReactElement<{ children: string }>).props.children).toBe('NDA123456')

    const templateElement = (columns[1] as { render: (_: unknown, record: Application) => unknown })
      .render(null, application)
    expect(isValidElement(templateElement)).toBe(true)
    expect((templateElement as ReactElement<{ color: string; children: string }>).props.color).toBe('blue')
    expect((templateElement as ReactElement<{ color: string; children: string }>).props.children).toBe('US FDA')

    expect((columns[4] as { render: (_: unknown, record: Application) => unknown }).render(null, application)).toBe(2)

    const actionsElement = (columns[5] as { render: (_: unknown, record: Application) => unknown })
      .render(null, application)
    expect(isValidElement(actionsElement)).toBe(true)

    const actionButtons = getActionButtons(actionsElement)
    expect(actionButtons).toHaveLength(2)
    expect(actionButtons[0].props.disabled).toBe(false)
    actionButtons[0].props.onClick()
    expect(onSelectApp).toHaveBeenCalledWith('app-1')

    expect(actionButtons[1].props.loading).toBe(false)
    expect(actionButtons[1].props.disabled).toBe(false)
    actionButtons[1].props.onClick()
    expect(onDeleteApp).toHaveBeenCalledWith('app-1')
  })

  it('disables application actions while deletion is running', () => {
    const columns = buildApplicationColumns({
      isBatchDeleteRunning: true,
      deletingAppIds: new Set<string>(['app-1']),
      onSelectApp: vi.fn(),
      onDeleteApp: vi.fn(),
    })

    const actionsElement = (columns[5] as { render: (_: unknown, record: Application) => unknown })
      .render(null, application)
    const actionButtons = getActionButtons(actionsElement)

    expect(actionButtons[0].props.disabled).toBe(true)
    expect(actionButtons[1].props.loading).toBe(true)
    expect(actionButtons[1].props.disabled).toBe(true)
  })
})
