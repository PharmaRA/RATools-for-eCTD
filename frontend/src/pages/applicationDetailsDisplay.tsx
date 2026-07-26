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
  { title: '序列', dataIndex: 'sequenceNumber', render: (value) => <b>{value}</b> },
  { title: '递交类型', dataIndex: 'submissionType' },
  { title: '描述', dataIndex: 'description' },
  {
    title: '操作',
    key: 'actions',
    render: (_, record) => {
      const isSequenceDeleteRunning = deletingSequenceNumbers.has(record.sequenceNumber)

      return (
        <Space>
          <Button type="link" size="small" disabled={isBatchDeleteRunning} onClick={() => onOpenWorkspace(record.sequenceNumber)}>
            进入工作区
          </Button>
          <Button
            danger
            type="text"
            size="small"
            icon={<Trash2 size={14} />}
            title="删除序列"
            loading={isSequenceDeleteRunning}
            disabled={isSequenceDeleteRunning || isBatchDeleteRunning}
            onClick={() => onDeleteSequence(record.sequenceNumber)}
          />
        </Space>
      )
    },
  },
]
