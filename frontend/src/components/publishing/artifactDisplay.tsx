import { Button, Tag } from 'antd'
import { Download } from 'lucide-react'

import { messages } from '../../i18n/messages'
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
  exists
    ? <Tag color="green">{messages.artifact.exists}</Tag>
    : <Tag color="red">{messages.artifact.missing}</Tag>
)

export const buildArtifactColumns = (jobId: string | null) => [
  { title: messages.artifact.columnName, dataIndex: 'name', key: 'name', render: (value: string) => <b>{value}</b> },
  { title: messages.artifact.columnStatus, dataIndex: 'exists', key: 'exists', render: renderArtifactExistsStatus },
  { title: messages.artifact.columnSize, dataIndex: 'sizeBytes', key: 'size', render: formatBytes },
  { title: messages.artifact.columnType, dataIndex: 'contentType', key: 'type' },
  {
    title: messages.artifact.columnAction,
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
          {messages.artifact.download}
        </Button>
      ) : <span className="text-gray-400">{messages.common.unavailable}</span>
    ),
  },
]
