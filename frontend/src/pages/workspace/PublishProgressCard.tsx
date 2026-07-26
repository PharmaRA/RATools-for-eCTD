import { Alert, Button, Card, Space, Spin, Tag } from 'antd'

import { buildPublishJobArtifactDownloadUrl } from '../../publishActions'
import type { PolledPublishJob } from './usePublishJobPolling'

type PublishProgressCardProps = {
  job: PolledPublishJob | null
  isPolling: boolean
  error: string | null
  onDismiss: () => void
}

const statusTagColor = (status?: string | null) => {
  switch (status) {
    case 'Completed':
      return 'green'
    case 'Failed':
      return 'red'
    case 'Running':
      return 'blue'
    default:
      return 'gold'
  }
}

const statusLabel = (status?: string | null) => {
  switch (status) {
    case 'Completed':
      return '已完成'
    case 'Failed':
      return '失败'
    case 'Running':
      return '执行中'
    case 'Pending':
      return '排队中'
    default:
      return status || '等待状态'
  }
}

export const PublishProgressCard = ({ job, isPolling, error, onDismiss }: PublishProgressCardProps) => {
  if (!job && !isPolling && !error) {
    return null
  }

  return (
    <Card size="small" title="发布进度" data-testid="publish-progress-card">
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2">
          {isPolling && <Spin size="small" />}
          <Tag color={statusTagColor(job?.status)} data-testid="publish-progress-status">
            {statusLabel(job?.status)}
          </Tag>
          {job?.id && <span className="text-gray-500 text-xs">作业 {job.id}</span>}
        </div>

        {error && (
          <Alert
            type="warning"
            showIcon
            title="进度查询失败"
            description={`无法继续查询发布状态：${error}。可稍后在“发布历史”页签查看结果。`}
          />
        )}

        {job?.status === 'Failed' && job.failureReason && (
          <Alert type="error" showIcon title="发布失败" description={job.failureReason} />
        )}

        {job?.status === 'Completed' && (
          <Space>
            <Button
              size="small"
              type="primary"
              href={buildPublishJobArtifactDownloadUrl(job.id, 'PackageZip')}
              target="_blank"
            >
              下载包
            </Button>
            <Button
              size="small"
              href={buildPublishJobArtifactDownloadUrl(job.id, 'PublishReport')}
              target="_blank"
            >
              下载报告
            </Button>
          </Space>
        )}

        {(job?.status === 'Completed' || job?.status === 'Failed' || error) && (
          <div>
            <Button size="small" onClick={onDismiss}>关闭</Button>
          </div>
        )}
      </div>
    </Card>
  )
}
