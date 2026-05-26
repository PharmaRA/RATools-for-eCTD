import { useEffect, useState } from 'react'
import { Button, Drawer, Spin, Table, Tag, message } from 'antd'
import { Download } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { formatBytes } from '../../pages/appShared'

export const ArtifactsPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false)
  const [artifacts, setArtifacts] = useState<any[]>([])

  useEffect(() => {
    if (!jobId) return
    setLoading(true)
    apiFetch(`/api/publish-jobs/${jobId}/artifacts`)
      .then((data) => setArtifacts(data.artifacts || []))
      .catch((err) => message.error('Failed to load artifacts: ' + err.message))
      .finally(() => setLoading(false))
  }, [jobId])

  const columns = [
    { title: 'Name', dataIndex: 'name', key: 'name', render: (t: string) => <b>{t}</b> },
    { title: 'Status', dataIndex: 'exists', key: 'exists', render: (exists: boolean) => exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag> },
    { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: (s: number) => formatBytes(s) },
    { title: 'Type', dataIndex: 'contentType', key: 'type' },
    {
      title: 'Action', key: 'action', render: (_: any, record: any) => (
        record.exists ? (
          <Button type="link" icon={<Download size={14} className="mr-1" />} href={`/api/publish-jobs/${jobId}/artifacts/${record.name}/download`} target="_blank" download>
            Download
          </Button>
        ) : <span className="text-gray-400">Unavailable</span>
      ),
    },
  ]

  return (
    <Drawer title="Publish Artifacts" placement="right" width={600} onClose={onClose} open={!!jobId}>
      {loading ? <Spin className="w-full mt-10 flex justify-center" /> : <Table dataSource={artifacts} columns={columns} rowKey="name" pagination={false} size="small" />}
    </Drawer>
  )
}
