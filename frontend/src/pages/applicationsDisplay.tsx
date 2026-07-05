import { Button, Space, Tag, type TableColumnsType } from 'antd'
import { Trash2 } from 'lucide-react'

import { type Application, formatDate, getApplicationTemplateLabel } from './appShared'

type BuildApplicationColumnsOptions = {
  isBatchDeleteRunning: boolean
  deletingAppIds: ReadonlySet<string>
  onSelectApp: (appId: string) => void
  onDeleteApp: (appId: string) => void
}

export const buildApplicationColumns = ({
  isBatchDeleteRunning,
  deletingAppIds,
  onSelectApp,
  onDeleteApp,
}: BuildApplicationColumnsOptions): TableColumnsType<Application> => [
  { title: 'App Number', dataIndex: 'applicationNumber', render: (value: string) => <b>{value}</b> },
  { title: 'eCTD Template', key: 'ectdTemplate', render: (_, record) => <Tag color="blue">{getApplicationTemplateLabel(record)}</Tag> },
  { title: 'Sponsor', dataIndex: 'sponsorName' },
  { title: 'Created', dataIndex: 'createdUtc', render: formatDate },
  { title: 'Sequences', key: 'sequences', render: (_, record) => record.sequences?.length || 0 },
  {
    title: 'Action',
    key: 'action',
    render: (_, record) => {
      const isAppDeleteRunning = deletingAppIds.has(record.id)

      return (
        <Space>
          <Button
            type="primary"
            size="small"
            disabled={isBatchDeleteRunning}
            onClick={() => onSelectApp(record.id)}
          >
            Manage App
          </Button>
          <Button
            danger
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete App"
            loading={isAppDeleteRunning}
            disabled={isAppDeleteRunning || isBatchDeleteRunning}
            onClick={() => onDeleteApp(record.id)}
          />
        </Space>
      )
    },
  },
]
