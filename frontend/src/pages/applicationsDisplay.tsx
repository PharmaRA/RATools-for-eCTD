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
  { title: '申请编号', dataIndex: 'applicationNumber', render: (value: string) => <b>{value}</b> },
  { title: 'eCTD 模板', key: 'ectdTemplate', render: (_, record) => <Tag color="blue">{getApplicationTemplateLabel(record)}</Tag> },
  { title: '申办方', dataIndex: 'sponsorName' },
  { title: '创建时间', dataIndex: 'createdUtc', render: formatDate },
  { title: '序列数', key: 'sequences', render: (_, record) => record.sequences?.length || 0 },
  {
    title: '操作',
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
            管理
          </Button>
          <Button
            danger
            size="small"
            icon={<Trash2 size={14} />}
            title="删除申请"
            loading={isAppDeleteRunning}
            disabled={isAppDeleteRunning || isBatchDeleteRunning}
            onClick={() => onDeleteApp(record.id)}
          />
        </Space>
      )
    },
  },
]
