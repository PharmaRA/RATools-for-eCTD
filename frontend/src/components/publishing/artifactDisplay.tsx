import { Button, Tag } from 'antd'
import { Download } from 'lucide-react'

import { formatBytes } from '../../pages/appShared'
import { buildPublishJobArtifactDownloadUrl } from '../../publishActions'

type PublishArtifactRow = {
  name: string
  exists: boolean
}

export const getPublishArtifactsFromResponse = <T,>(
  response?: { artifacts?: T[] | null } | null,
): T[] => response?.artifacts || []

export const renderArtifactExistsStatus = (exists?: boolean | null) => (
  exists ? <Tag color="green">存在</Tag> : <Tag color="red">缺失</Tag>
)

export const buildArtifactColumns = (jobId: string | null) => [
  { title: '名称', dataIndex: 'name', key: 'name', render: (value: string) => <b>{value}</b> },
  { title: '状态', dataIndex: 'exists', key: 'exists', render: renderArtifactExistsStatus },
  { title: '大小', dataIndex: 'sizeBytes', key: 'size', render: formatBytes },
  { title: '类型', dataIndex: 'contentType', key: 'type' },
  {
    title: '操作',
    key: 'action',
    render: (_: unknown, record: PublishArtifactRow) => (
      record.exists ? (
        <Button
          type="link"
          icon={<Download size={14} className="mr-1" />}
          href={buildPublishJobArtifactDownloadUrl(jobId, record.name)}
          target="_blank"
          download
        >
          下载
        </Button>
      ) : <span className="text-gray-400">不可用</span>
    ),
  },
]
