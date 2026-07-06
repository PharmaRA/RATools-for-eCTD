import { useEffect, useState } from 'react'
import { Drawer, Spin, Table, message } from 'antd'

import { getErrorMessage } from '../../pages/appShared'
import { loadPublishJobArtifacts } from '../../publishActions'
import { buildArtifactColumns, getPublishArtifactsFromResponse } from './artifactDisplay'

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
        const data = await loadPublishJobArtifacts<PublishArtifactsResponse>(jobId)
        if (active) setArtifacts(getPublishArtifactsFromResponse(data))
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

  return (
    <Drawer title="Publish Artifacts" placement="right" size={600} onClose={onClose} open={!!jobId}>
      {loading ? <Spin className="w-full mt-10 flex justify-center" /> : <Table dataSource={artifacts} columns={buildArtifactColumns(jobId)} rowKey="name" pagination={false} size="small" />}
    </Drawer>
  )
}
