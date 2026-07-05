import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'

import {
  buildPublishHistoryColumns,
  type PublishHistoryEntry,
} from './publishHistoryTableDisplay'

type ColumnMetadata = {
  title?: string
  dataIndex?: string
  key?: string
  width?: number
}

type ActionButtonProps = {
  onClick: () => void
}

const entry: PublishHistoryEntry = {
  publishJobId: 'job-1',
  sequenceNumber: '0001',
  status: 'Completed',
  validationProfile: 'FDA',
  errorCount: 1,
  warningCount: 2,
  warningSummary: 'Two warning categories',
  lifecycleSummary: {
    replaceTargetNotFoundCount: 1,
  },
  artifactSummary: {
    fileCount: 3,
    packageSizeBytes: 1536,
  },
  reportAvailable: true,
  reportReadable: true,
  createdUtc: '2025-01-01T00:00:00Z',
  publishReadiness: {
    isReady: false,
    status: 'Blocked',
    blockingErrorCount: 1,
    warningCount: 2,
    missingMetadataFields: ['sequenceDescription'],
  },
}

const getColumnMetadata = (column: unknown) => {
  const { title, dataIndex, key, width } = column as ColumnMetadata
  return { title, dataIndex, key, width }
}

const getActionButtons = (element: unknown) => (
  element as ReactElement<{ children: ReactElement<ActionButtonProps>[] }>
).props.children

describe('publishHistoryTableDisplay', () => {
  it('builds publish history table columns', () => {
    const columns = buildPublishHistoryColumns({
      onOpenReview: vi.fn(),
      onOpenReport: vi.fn(),
      onOpenArtifacts: vi.fn(),
    })

    expect(columns.map(getColumnMetadata)).toEqual([
      { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'seq', width: undefined },
      { title: 'Status', dataIndex: 'status', key: 'status', width: undefined },
      { title: 'Profile', dataIndex: 'validationProfile', key: 'profile', width: undefined },
      { title: 'Validation', dataIndex: undefined, key: 'validation', width: 220 },
      { title: 'Readiness', dataIndex: undefined, key: 'readiness', width: 180 },
      { title: 'Lifecycle', dataIndex: undefined, key: 'lifecycle', width: 160 },
      { title: 'Artifacts', dataIndex: undefined, key: 'artifacts', width: 180 },
      { title: 'Report', dataIndex: undefined, key: 'report', width: 180 },
      { title: 'Created', dataIndex: 'createdUtc', key: 'created', width: undefined },
      { title: 'Actions', dataIndex: undefined, key: 'actions', width: 260 },
    ])

    const statusElement = (columns[1] as { render: (value: string) => unknown }).render('Completed')
    expect(isValidElement(statusElement)).toBe(true)
    expect((statusElement as ReactElement<{ status: string; text: string }>).props.status).toBe('success')
    expect((statusElement as ReactElement<{ status: string; text: string }>).props.text).toBe('Completed')

    const readinessElement = (columns[4] as { render: (_: unknown, record: PublishHistoryEntry) => unknown })
      .render(null, entry)
    expect(isValidElement(readinessElement)).toBe(true)

    expect((columns[5] as { render: (_: unknown, record: PublishHistoryEntry) => unknown }).render(null, entry)).toBe('1 issues')

    const actionsElement = (columns[9] as { render: (_: unknown, record: PublishHistoryEntry) => unknown })
      .render(null, entry)
    expect(isValidElement(actionsElement)).toBe(true)
  })

  it('wires publish history action buttons to the publish job id', () => {
    const onOpenReview = vi.fn()
    const onOpenReport = vi.fn()
    const onOpenArtifacts = vi.fn()
    const columns = buildPublishHistoryColumns({
      onOpenReview,
      onOpenReport,
      onOpenArtifacts,
    })

    const actionsElement = (columns[9] as { render: (_: unknown, record: PublishHistoryEntry) => unknown })
      .render(null, entry)
    const actionButtons = getActionButtons(actionsElement)

    expect(actionButtons).toHaveLength(3)
    actionButtons[0].props.onClick()
    actionButtons[1].props.onClick()
    actionButtons[2].props.onClick()

    expect(onOpenReview).toHaveBeenCalledWith('job-1')
    expect(onOpenReport).toHaveBeenCalledWith('job-1')
    expect(onOpenArtifacts).toHaveBeenCalledWith('job-1')
  })
})
