import { Button, Space, type TableColumnsType } from 'antd'
import { Trash2 } from 'lucide-react'

import { type SequenceSummary } from './appShared'

type ApplicationDetailsTitle = {
  applicationNumber: string
  sponsorName: string
}

export const formatApplicationDetailsTitle = (application: ApplicationDetailsTitle | null, fallbackAppId: string) => {
  return application ? `${application.applicationNumber} (${application.sponsorName})` : fallbackAppId
}

export const getApplicationSequences = <T,>(
  application?: { sequences?: T[] | null } | null,
): T[] => application?.sequences || []

type BuildSequenceColumnsOptions = {
  isBatchDeleteRunning: boolean
  deletingSequenceNumbers: ReadonlySet<string>
  onOpenWorkspace: (sequenceNumber: string) => void
  onDeleteSequence: (sequenceNumber: string) => void
}

export const buildSequenceColumns = ({
  isBatchDeleteRunning,
  deletingSequenceNumbers,
  onOpenWorkspace,
  onDeleteSequence,
}: BuildSequenceColumnsOptions): TableColumnsType<SequenceSummary> => [
  { title: 'Sequence', dataIndex: 'sequenceNumber', render: (value) => <b>{value}</b> },
  { title: 'Submission Type', dataIndex: 'submissionType' },
  { title: 'Description', dataIndex: 'description' },
  {
    title: 'Actions',
    key: 'actions',
    render: (_, record) => {
      const isSequenceDeleteRunning = deletingSequenceNumbers.has(record.sequenceNumber)

      return (
        <Space>
          <Button type="link" size="small" disabled={isBatchDeleteRunning} onClick={() => onOpenWorkspace(record.sequenceNumber)}>
            Enter Workspace
          </Button>
          <Button
            danger
            type="text"
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete Sequence"
            loading={isSequenceDeleteRunning}
            disabled={isSequenceDeleteRunning || isBatchDeleteRunning}
            onClick={() => onDeleteSequence(record.sequenceNumber)}
          />
        </Space>
      )
    },
  },
]
