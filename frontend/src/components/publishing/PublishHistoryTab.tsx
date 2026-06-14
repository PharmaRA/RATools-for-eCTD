import { useEffect, useState } from 'react'
import { Badge, Button, Card, Col, Form, Input, Row, Select, Space, Statistic, Table, Tag, message } from 'antd'

import { apiFetch } from '../../apiClient'
import { formatBytes, formatDate, getLifecycleIssueCount, getReportAvailabilityLabel, getStatusColor } from '../../pages/appShared'
import { ArtifactsPanel } from './ArtifactsPanel'
import { PackageReviewPanel } from './PackageReviewPanel'
import { ReportPanel } from './ReportPanel'

const readinessSortOptions = ['blocked-first', 'ready-first'] as const

const getInitialQueryState = () => {
  const params = new URLSearchParams(window.location.search)
  const readinessSort = params.get('publishReadinessSort')

  return {
    formValues: {
      sequenceNumber: params.get('publishSequenceNumber') || undefined,
      status: params.get('publishStatus') || undefined,
      readinessStatus: params.get('publishReadinessStatus') || undefined,
      readinessSort: readinessSort && readinessSortOptions.includes(readinessSort as any) ? readinessSort : undefined,
    },
    readinessSort: readinessSort && readinessSortOptions.includes(readinessSort as any) ? readinessSort : null,
  }
}

export const PublishHistoryTab = ({ appId }: { appId: string }) => {
  const [initialQueryState] = useState(getInitialQueryState)
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<any>(null)
  const [selectedReviewJobId, setSelectedReviewJobId] = useState<string | null>(null)
  const [selectedReportJobId, setSelectedReportJobId] = useState<string | null>(null)
  const [selectedArtifactsJobId, setSelectedArtifactsJobId] = useState<string | null>(null)
  const [form] = Form.useForm()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [readinessSort, setReadinessSort] = useState<string | null>(initialQueryState.readinessSort)

  const replaceBrowserQuery = (values: any, nextReadinessSort: string | null) => {
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

  const fetchHistory = () => {
    setLoading(true)
    const values = form.getFieldsValue()
    const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
    if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber)
    if (values.status) params.append('status', values.status)
    if (values.readinessStatus) params.append('readinessStatus', values.readinessStatus)

    apiFetch(`/api/applications/${appId}/publish-history?${params.toString()}`)
      .then((res) => setData(res))
      .catch((err) => message.error('Failed to load history: ' + err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => { fetchHistory() }, [appId, page, pageSize])

  const applyFilters = () => {
    const values = form.getFieldsValue()
    const nextReadinessSort = values.readinessSort === 'default' ? null : values.readinessSort || null
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

    const missingMetadataFields = readiness.missingMetadataFields || []
    const primaryHint = missingMetadataFields[0]
    const additionalCount = Math.max(0, missingMetadataFields.length - 1)

    return (
      <div>
        <div>
          <Tag color={readiness.isReady ? 'green' : 'red'}>{readiness.status || (readiness.isReady ? 'Ready' : 'Blocked')}</Tag>
        </div>
        {!readiness.isReady && primaryHint && (
          <div className="text-gray-500 text-xs">
            {primaryHint}
            {additionalCount > 0 ? ` +${additionalCount}` : ''}
          </div>
        )}
        {readiness.isReady && (readiness.warningCount || 0) > 0 && (
          <div className="text-gray-500 text-xs">{`Warnings: ${readiness.warningCount}`}</div>
        )}
        {!readiness.isReady && !primaryHint && (readiness.blockingErrorCount || 0) > 0 && (
          <div className="text-gray-500 text-xs">{`Blocking errors: ${readiness.blockingErrorCount}`}</div>
        )}
      </div>
    )
  }

  const getReadinessSortRank = (readiness?: { status?: string } | null) => {
    const status = readiness?.status?.toLowerCase()
    if (status === 'blocked') return 0
    if (!status || status === 'unknown') return 1
    if (status === 'ready') return 2
    return 3
  }

  const getSortedEntries = () => {
    const entries = [...(data?.entries || [])]
    if (!readinessSort) return entries

    return entries.sort((left, right) => {
      const leftRank = getReadinessSortRank(left.publishReadiness)
      const rightRank = getReadinessSortRank(right.publishReadiness)
      if (leftRank === rightRank) return 0

      if (readinessSort === 'blocked-first') {
        return leftRank - rightRank
      }

      return rightRank - leftRank
    })
  }

  const columns = [
    { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'seq' },
    { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Badge status={getStatusColor(s) as any} text={s} /> },
    { title: 'Profile', dataIndex: 'validationProfile', key: 'profile' },
    {
      title: 'Validation', key: 'validation', width: 220,
      render: (_: any, r: any) => (
        <div>
          <div>{`Errors: ${r.errorCount ?? 0}`}</div>
          <div>{`Warnings: ${r.warningCount ?? 0}`}</div>
          {r.warningSummary && <div className="text-gray-500 text-xs">{r.warningSummary}</div>}
        </div>
      ),
    },
    {
      title: 'Readiness', key: 'readiness', width: 180,
      render: (_: any, r: any) => renderReadiness(r.publishReadiness),
    },
    {
      title: 'Lifecycle', key: 'lifecycle', width: 160,
      render: (_: any, r: any) => {
        const issueCount = getLifecycleIssueCount(r.lifecycleSummary)
        return issueCount === 0 ? 'All matched' : `${issueCount} issues`
      },
    },
    {
      title: 'Artifacts', key: 'artifacts', width: 180,
      render: (_: any, r: any) => (
        <div>
          <div>{r.artifactSummary ? `${r.artifactSummary.fileCount} files` : '-'}</div>
          {r.artifactSummary && <div className="text-gray-500 text-xs">{formatBytes(r.artifactSummary.packageSizeBytes || 0)}</div>}
        </div>
      ),
    },
    {
      title: 'Report', key: 'report', width: 180,
      render: (_: any, r: any) => (
        <div>
          <div>{getReportAvailabilityLabel(r)}</div>
          {r.reportError && <div className="text-gray-500 text-xs">{r.reportError}</div>}
        </div>
      ),
    },
    { title: 'Created', dataIndex: 'createdUtc', key: 'created', render: formatDate },
    {
      title: 'Actions', key: 'actions', fixed: 'right' as const, width: 260,
      render: (_: any, r: any) => (
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
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Completed Jobs" value={data.statusSummary.completedCount} styles={{ content: { color: '#3f8600' } }} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Failed Jobs" value={data.statusSummary.failedCount} styles={{ content: { color: '#cf1322' } }} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Running Jobs" value={data.statusSummary.runningCount} styles={{ content: { color: '#1677ff' } }} /></Card></Col>
        </Row>
      )}
      {data?.readinessSummary && (
        <Row gutter={16}>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Ready Sequences" value={data.readinessSummary.readyCount} styles={{ content: { color: '#3f8600' } }} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Blocked Sequences" value={data.readinessSummary.blockedCount} styles={{ content: { color: '#cf1322' } }} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Unknown Readiness" value={data.readinessSummary.unknownCount} styles={{ content: { color: '#595959' } }} /></Card></Col>
        </Row>
      )}
      {data?.lifecycleSummary && (
        <Row gutter={16}>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Matched" value={data.lifecycleSummary.matchedCount} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Replace Missing" value={data.lifecycleSummary.replaceTargetNotFoundCount} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Delete Missing" value={data.lifecycleSummary.deleteTargetNotFoundCount} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Append Missing" value={data.lifecycleSummary.appendTargetNotFoundCount} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Ambiguous" value={data.lifecycleSummary.ambiguousCount} /></Card></Col>
          <Col span={8}><Card size="small" variant="outlined" className="shadow-sm"><Statistic title="Current Sequence" value={data.lifecycleSummary.currentSequenceCount} /></Card></Col>
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
        <Table loading={loading} dataSource={getSortedEntries()} columns={columns} rowKey="publishJobId" size="small"
          pagination={{ current: page, pageSize, total: data?.totalCount || 0, showSizeChanger: true, onChange: (p, ps) => { setPage(p); setPageSize(ps) } }}
        />
      </div>
      <PackageReviewPanel jobId={selectedReviewJobId} onClose={() => setSelectedReviewJobId(null)} />
      <ReportPanel jobId={selectedReportJobId} onClose={() => setSelectedReportJobId(null)} />
      <ArtifactsPanel jobId={selectedArtifactsJobId} onClose={() => setSelectedArtifactsJobId(null)} />
    </div>
  )
}
