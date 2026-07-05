import { useEffect, useState } from 'react'
import { Button, Drawer, Spin, Table, message } from 'antd'
import { Download } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { formatBytes, getErrorMessage } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'

type PublishArtifact = {
  name: string
  exists: boolean
  sizeBytes: number
  contentType?: string | null
}

type PublishArtifactsResponse = {
  artifacts?: PublishArtifact[]
}

export const ArtifactsPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false)
  const [artifacts, setArtifacts] = useState<PublishArtifact[]>([])

  useEffect(() => {
    if (!jobId) return

    let active = true

    void Promise.resolve().then(async () => {
      setLoading(true)
      try {
        const data = await apiFetch(`/api/publish-jobs/${jobId}/artifacts`) as PublishArtifactsResponse
        if (active) setArtifacts(data.artifacts || [])
      } catch (error) {
        if (active) message.error('Failed to load artifacts: ' + getErrorMessage(error))
      } finally {
        if (active) setLoading(false)
      }
    })

    return () => {
      active = false
    }
  }, [jobId])

  const columns = [
    { title: 'Name', dataIndex: 'name', key: 'name', render: (t: string) => <b>{t}</b> },
    { title: 'Status', dataIndex: 'exists', key: 'exists', render: renderArtifactExistsStatus },
    { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: (s: number) => formatBytes(s) },
    { title: 'Type', dataIndex: 'contentType', key: 'type' },
    {
      title: 'Action', key: 'action', render: (_: unknown, record: PublishArtifact) => (
        record.exists ? (
          <Button type="link" icon={<Download size={14} className="mr-1" />} href={`/api/publish-jobs/${jobId}/artifacts/${record.name}/download`} target="_blank" download>
            Download
          </Button>
        ) : <span className="text-gray-400">Unavailable</span>
      ),
    },
  ]

  return (
    <Drawer title="Publish Artifacts" placement="right" size={600} onClose={onClose} open={!!jobId}>
      {loading ? <Spin className="w-full mt-10 flex justify-center" /> : <Table dataSource={artifacts} columns={columns} rowKey="name" pagination={false} size="small" />}
    </Drawer>
  )
}
