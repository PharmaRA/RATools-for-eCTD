import { Badge, Button, Space, Tag, type TableColumnsType } from 'antd'

import {
  formatDate,
  getReportAvailabilityLabel,
  getStatusColor,
  type LifecycleSummary,
  type ReportAvailability,
} from '../../pages/appShared'
import {
  formatReadinessHistoryCountHint,
  formatReadinessMissingMetadataHint,
  getPublishReadinessStatusTagProps,
} from './publishReadinessDisplay'
import {
  buildPublishHistoryValidationSummaryItems,
  formatArtifactFileCount,
  formatArtifactPackageSize,
  formatPublishHistoryLifecycleStatus,
} from './publishHistoryDisplay'

export type PublishReadinessSummary = {
  isReady?: boolean
  status?: string
  blockingErrorCount?: number
  warningCount?: number
  missingMetadataFields?: string[]
}

type ArtifactSummary = {
  fileCount?: number | null
  packageSizeBytes?: number | null
}

export type PublishHistoryEntry = ReportAvailability & {
  publishJobId: string
  sequenceNumber: string
  status: string
  validationProfile?: string | null
  errorCount?: number | null
  warningCount?: number | null
  warningSummary?: string | null
  lifecycleSummary?: LifecycleSummary | null
  artifactSummary?: ArtifactSummary | null
  reportError?: string | null
  createdUtc?: string | null
  publishReadiness?: PublishReadinessSummary | null
}

type BuildPublishHistoryColumnsOptions = {
  onOpenReview: (publishJobId: string) => void
  onOpenReport: (publishJobId: string) => void
  onOpenArtifacts: (publishJobId: string) => void
}

const renderReadiness = (readiness?: PublishReadinessSummary | null) => {
  if (!readiness) return '-'

  const missingMetadataHint = formatReadinessMissingMetadataHint(readiness.missingMetadataFields)
  const readinessCountHint = formatReadinessHistoryCountHint(readiness, missingMetadataHint)
  const statusTag = getPublishReadinessStatusTagProps(readiness)

  return (
    <div>
      <div>
        <Tag color={statusTag.color}>{statusTag.label}</Tag>
      </div>
      {!readiness.isReady && missingMetadataHint && (
        <div className="text-gray-500 text-xs">
          {missingMetadataHint}
        </div>
      )}
      {readinessCountHint && (
        <div className="text-gray-500 text-xs">{readinessCountHint}</div>
      )}
    </div>
  )
}

export const buildPublishHistoryColumns = ({
  onOpenReview,
  onOpenReport,
  onOpenArtifacts,
}: BuildPublishHistoryColumnsOptions): TableColumnsType<PublishHistoryEntry> => [
  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'seq' },
  { title: 'Status', dataIndex: 'status', key: 'status', render: (status: string) => <Badge status={getStatusColor(status)} text={status} /> },
  { title: 'Profile', dataIndex: 'validationProfile', key: 'profile' },
  {
    title: 'Validation',
    key: 'validation',
    width: 220,
    render: (_, record) => (
      <div>
        {buildPublishHistoryValidationSummaryItems(record).map((item) => (
          <div key={item.label}>{`${item.label}: ${item.value}`}</div>
        ))}
        {record.warningSummary && <div className="text-gray-500 text-xs">{record.warningSummary}</div>}
      </div>
    ),
  },
  {
    title: 'Readiness',
    key: 'readiness',
    width: 180,
    render: (_, record) => renderReadiness(record.publishReadiness),
  },
  {
    title: 'Lifecycle',
    key: 'lifecycle',
    width: 160,
    render: (_, record) => formatPublishHistoryLifecycleStatus(record.lifecycleSummary),
  },
  {
    title: 'Artifacts',
    key: 'artifacts',
    width: 180,
    render: (_, record) => {
      const packageSize = formatArtifactPackageSize(record.artifactSummary)
      return (
        <div>
          <div>{formatArtifactFileCount(record.artifactSummary?.fileCount)}</div>
          {packageSize && <div className="text-gray-500 text-xs">{packageSize}</div>}
        </div>
      )
    },
  },
  {
    title: 'Report',
    key: 'report',
    width: 180,
    render: (_, record) => (
      <div>
        <div>{getReportAvailabilityLabel(record)}</div>
        {record.reportError && <div className="text-gray-500 text-xs">{record.reportError}</div>}
      </div>
    ),
  },
  { title: 'Created', dataIndex: 'createdUtc', key: 'created', render: formatDate },
  {
    title: 'Actions',
    key: 'actions',
    fixed: 'right' as const,
    width: 260,
    render: (_, record) => (
      <Space>
        <Button size="small" type="primary" onClick={() => onOpenReview(record.publishJobId)}>Review</Button>
        <Button size="small" onClick={() => onOpenReport(record.publishJobId)}>Report</Button>
        <Button size="small" type="primary" ghost onClick={() => onOpenArtifacts(record.publishJobId)}>Artifacts</Button>
      </Space>
    ),
  },
]
