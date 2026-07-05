import { useCallback, useEffect, useState } from 'react'
import { Badge, Button, Card, Col, Form, Input, Row, Select, Space, Statistic, Table, Tag, message, type TableColumnsType } from 'antd'

import { apiFetch } from '../../apiClient'
import { formatDate, getErrorMessage, getReportAvailabilityLabel, getStatusColor, type LifecycleSummary, type ReportAvailability } from '../../pages/appShared'
import { ArtifactsPanel } from './ArtifactsPanel'
import { PackageReviewPanel } from './PackageReviewPanel'
import { formatReadinessHistoryCountHint, formatReadinessMissingMetadataHint, getPublishReadinessStatusTagProps } from './publishReadinessDisplay'
import { isReadinessSort, sortPublishHistoryEntries, type ReadinessSort } from './publishHistorySorting'
import {
  buildPublishHistoryLifecycleStatisticItems,
  buildPublishHistoryReadinessStatisticItems,
  buildPublishHistoryStatusStatisticItems,
  buildPublishHistoryValidationSummaryItems,
  formatArtifactFileCount,
  formatArtifactPackageSize,
  formatPublishHistoryLifecycleStatus,
} from './publishHistoryDisplay'
import { ReportPanel } from './ReportPanel'

type PublishHistoryFilterValues = {
  sequenceNumber?: string
  status?: string
  readinessStatus?: string
  readinessSort?: ReadinessSort | 'default' | null
}

type PublishReadinessSummary = {
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

type PublishHistoryEntry = ReportAvailability & {
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

type PublishHistoryResponse = {
  entries?: PublishHistoryEntry[]
  totalCount?: number
  statusSummary?: {
    completedCount?: number
    failedCount?: number
    runningCount?: number
  }
  readinessSummary?: {
    readyCount?: number
    blockedCount?: number
    unknownCount?: number
  }
  lifecycleSummary?: LifecycleSummary & {
    matchedCount?: number
  }
}

const getInitialQueryState = () => {
  const params = new URLSearchParams(window.location.search)
  const readinessSort = params.get('publishReadinessSort')
  const validatedReadinessSort = isReadinessSort(readinessSort) ? readinessSort : null

  return {
    formValues: {
      sequenceNumber: params.get('publishSequenceNumber') || undefined,
      status: params.get('publishStatus') || undefined,
      readinessStatus: params.get('publishReadinessStatus') || undefined,
      readinessSort: validatedReadinessSort || undefined,
    },
    readinessSort: validatedReadinessSort,
  }
}

export const PublishHistoryTab = ({ appId }: { appId: string }) => {
  const [initialQueryState] = useState(getInitialQueryState)
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<PublishHistoryResponse | null>(null)
  const [selectedReviewJobId, setSelectedReviewJobId] = useState<string | null>(null)
  const [selectedReportJobId, setSelectedReportJobId] = useState<string | null>(null)
  const [selectedArtifactsJobId, setSelectedArtifactsJobId] = useState<string | null>(null)
  const [form] = Form.useForm<PublishHistoryFilterValues>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [readinessSort, setReadinessSort] = useState<ReadinessSort | null>(initialQueryState.readinessSort)

  const replaceBrowserQuery = (values: PublishHistoryFilterValues, nextReadinessSort: ReadinessSort | null) => {
    const params = new URLSearchParams(window.location.search)
    params.delete('publishSequenceNumber')
    params.delete('publishStatus')
    params.delete('publishReadinessStatus')
    params.delete('publishReadinessSort')

    if (values.sequenceNumber) params.set('publishSequenceNumber', values.sequenceNumber)
    if (values.status) params.set('publishStatus', values.status)
    if (values.readinessStatus) params.set('publishReadinessStatus', values.readinessStatus)
    if (nextReadinessSort) params.set('publishReadinessSort', nextReadinessSort)

    const nextSearch = params.toString()
    window.history.replaceState(null, '', `${window.location.pathname}${nextSearch ? `?${nextSearch}` : ''}${window.location.hash}`)
  }

  const fetchHistory = useCallback(async () => {
    setLoading(true)
    const values = form.getFieldsValue()
    const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
    if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber)
    if (values.status) params.append('status', values.status)
    if (values.readinessStatus) params.append('readinessStatus', values.readinessStatus)

    try {
      const res = await apiFetch(`/api/applications/${appId}/publish-history?${params.toString()}`) as PublishHistoryResponse
      setData(res)
    } catch (err) {
      message.error('Failed to load history: ' + getErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }, [appId, form, page, pageSize])

  useEffect(() => {
    void Promise.resolve().then(fetchHistory)
  }, [fetchHistory])

  const applyFilters = () => {
    const values = form.getFieldsValue()
    const selectedReadinessSort = values.readinessSort || null
    const nextReadinessSort = isReadinessSort(selectedReadinessSort) ? selectedReadinessSort : null
    setReadinessSort(nextReadinessSort)
    replaceBrowserQuery(values, nextReadinessSort)

    if (page !== 1) {
      setPage(1)
      return
    }

    fetchHistory()
  }

  const resetFilters = () => {
    form.resetFields()
    setReadinessSort(null)
    replaceBrowserQuery({}, null)

    if (page !== 1) {
      setPage(1)
      return
    }

    fetchHistory()
  }

  const renderReadiness = (readiness?: {
    isReady?: boolean
    status?: string
    blockingErrorCount?: number
    warningCount?: number
    missingMetadataFields?: string[]
  } | null) => {
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

  const getSortedEntries = () => {
    return sortPublishHistoryEntries(data?.entries || [], readinessSort)
  }

  const columns: TableColumnsType<PublishHistoryEntry> = [
    { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'seq' },
    { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Badge status={getStatusColor(s)} text={s} /> },
    { title: 'Profile', dataIndex: 'validationProfile', key: 'profile' },
    {
      title: 'Validation', key: 'validation', width: 220,
      render: (_, r) => (
        <div>
          {buildPublishHistoryValidationSummaryItems(r).map((item) => (
            <div key={item.label}>{`${item.label}: ${item.value}`}</div>
          ))}
          {r.warningSummary && <div className="text-gray-500 text-xs">{r.warningSummary}</div>}
        </div>
      ),
    },
    {
      title: 'Readiness', key: 'readiness', width: 180,
      render: (_, r) => renderReadiness(r.publishReadiness),
    },
    {
      title: 'Lifecycle', key: 'lifecycle', width: 160,
      render: (_, r) => formatPublishHistoryLifecycleStatus(r.lifecycleSummary),
    },
    {
      title: 'Artifacts', key: 'artifacts', width: 180,
      render: (_, r) => {
        const packageSize = formatArtifactPackageSize(r.artifactSummary)
        return (
          <div>
            <div>{formatArtifactFileCount(r.artifactSummary?.fileCount)}</div>
            {packageSize && <div className="text-gray-500 text-xs">{packageSize}</div>}
          </div>
        )
      },
    },
    {
      title: 'Report', key: 'report', width: 180,
      render: (_, r) => (
        <div>
          <div>{getReportAvailabilityLabel(r)}</div>
          {r.reportError && <div className="text-gray-500 text-xs">{r.reportError}</div>}
        </div>
      ),
    },
    { title: 'Created', dataIndex: 'createdUtc', key: 'created', render: formatDate },
    {
      title: 'Actions', key: 'actions', fixed: 'right' as const, width: 260,
      render: (_, r) => (
        <Space>
          <Button size="small" type="primary" onClick={() => setSelectedReviewJobId(r.publishJobId)}>Review</Button>
          <Button size="small" onClick={() => setSelectedReportJobId(r.publishJobId)}>Report</Button>
          <Button size="small" type="primary" ghost onClick={() => setSelectedArtifactsJobId(r.publishJobId)}>Artifacts</Button>
        </Space>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      {data?.statusSummary && (
        <Row gutter={16}>
          {buildPublishHistoryStatusStatisticItems(data.statusSummary).map((item) => (
            <Col span={8} key={item.title}>
              <Card size="small" variant="outlined" className="shadow-sm">
                <Statistic title={item.title} value={item.value} styles={{ content: { color: item.color } }} />
              </Card>
            </Col>
          ))}
        </Row>
      )}
      {data?.readinessSummary && (
        <Row gutter={16}>
          {buildPublishHistoryReadinessStatisticItems(data.readinessSummary).map((item) => (
            <Col span={8} key={item.title}>
              <Card size="small" variant="outlined" className="shadow-sm">
                <Statistic title={item.title} value={item.value} styles={{ content: { color: item.color } }} />
              </Card>
            </Col>
          ))}
        </Row>
      )}
      {data?.lifecycleSummary && (
        <Row gutter={16}>
          {buildPublishHistoryLifecycleStatisticItems(data.lifecycleSummary).map((item) => (
            <Col span={8} key={item.title}>
              <Card size="small" variant="outlined" className="shadow-sm">
                <Statistic title={item.title} value={item.value} />
              </Card>
            </Col>
          ))}
        </Row>
      )}
      <div className="bg-white p-4 rounded border border-gray-200">
        <Form form={form} layout="inline" onFinish={applyFilters} className="mb-4" initialValues={initialQueryState.formValues}>
          <Form.Item name="sequenceNumber" label="Sequence"><Input placeholder="e.g. 0000" allowClear className="w-32" /></Form.Item>
          <Form.Item name="status" label="Status">
            <Select placeholder="All" allowClear className="w-32">
              <Select.Option value="Completed">Completed</Select.Option>
              <Select.Option value="Failed">Failed</Select.Option>
              <Select.Option value="Running">Running</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="readinessStatus" label="Readiness">
            <Select placeholder="All" allowClear className="w-36">
              <Select.Option value="Ready">Ready</Select.Option>
              <Select.Option value="Blocked">Blocked</Select.Option>
              <Select.Option value="Unknown">Unknown</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="readinessSort" label="Sort">
            <Select placeholder="Default" allowClear className="w-40">
              <Select.Option value="default">Default</Select.Option>
              <Select.Option value="blocked-first">Blocked first</Select.Option>
              <Select.Option value="ready-first">Ready first</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item><Button type="primary" htmlType="submit">Filter</Button><Button className="ml-2" onClick={resetFilters}>Reset</Button></Form.Item>
        </Form>
        <Table<PublishHistoryEntry> loading={loading} dataSource={getSortedEntries()} columns={columns} rowKey="publishJobId" size="small"
          pagination={{ current: page, pageSize, total: data?.totalCount || 0, showSizeChanger: true, onChange: (p, ps) => { setPage(p); setPageSize(ps) } }}
        />
      </div>
      <PackageReviewPanel jobId={selectedReviewJobId} onClose={() => setSelectedReviewJobId(null)} />
      <ReportPanel jobId={selectedReportJobId} onClose={() => setSelectedReportJobId(null)} />
      <ArtifactsPanel jobId={selectedArtifactsJobId} onClose={() => setSelectedArtifactsJobId(null)} />
    </div>
  )
}
