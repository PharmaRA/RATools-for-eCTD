import { Button, Tag } from 'antd'
import { Download } from 'lucide-react'

import { formatBytes } from '../../pages/appShared'

type PublishArtifactRow = {
  name: string
  exists: boolean
}

export const getPublishArtifactsFromResponse = <T,>(
  response?: { artifacts?: T[] | null } | null,
): T[] => response?.artifacts || []

export const renderArtifactExistsStatus = (exists?: boolean | null) => (
  exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag>
)

export const buildArtifactColumns = (jobId: string | null) => [
  { title: 'Name', dataIndex: 'name', key: 'name', render: (value: string) => <b>{value}</b> },
  { title: 'Status', dataIndex: 'exists', key: 'exists', render: renderArtifactExistsStatus },
  { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: formatBytes },
  { title: 'Type', dataIndex: 'contentType', key: 'type' },
  {
    title: 'Action',
    key: 'action',
    render: (_: unknown, record: PublishArtifactRow) => (
      record.exists ? (
        <Button
          type="link"
          icon={<Download size={14} className="mr-1" />}
          href={`/api/publish-jobs/${jobId}/artifacts/${record.name}/download`}
          target="_blank"
          download
        >
          Download
        </Button>
      ) : <span className="text-gray-400">Unavailable</span>
    ),
  },
]
