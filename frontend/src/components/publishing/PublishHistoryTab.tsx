import { useCallback, useEffect, useState } from 'react'
import { Button, Card, Col, Form, Input, Row, Select, Statistic, Table, message } from 'antd'

import { apiFetch } from '../../apiClient'
import { getErrorMessage, type LifecycleSummary } from '../../pages/appShared'
import { ArtifactsPanel } from './ArtifactsPanel'
import { PackageReviewPanel } from './PackageReviewPanel'
import { getPublishHistoryEntriesFromResponse, sortPublishHistoryEntries, type ReadinessSort } from './publishHistorySorting'
import { buildPublishHistoryBrowserUrl, buildPublishHistoryRequestUrl, getPublishHistoryInitialQueryState, normalizePublishHistoryReadinessSort, type PublishHistoryFilterValues } from './publishHistoryQueryState'
import {
  buildPublishHistoryLifecycleStatisticItems,
  buildPublishHistoryReadinessStatisticItems,
  buildPublishHistoryStatusStatisticItems,
} from './publishHistoryDisplay'
import { buildPublishHistoryColumns, type PublishHistoryEntry } from './publishHistoryTableDisplay'
import { ReportPanel } from './ReportPanel'

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

export const PublishHistoryTab = ({ appId }: { appId: string }) => {
  const [initialQueryState] = useState(() => getPublishHistoryInitialQueryState(window.location.search))
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
    window.history.replaceState(null, '', buildPublishHistoryBrowserUrl(
      window.location.pathname,
      window.location.search,
      window.location.hash,
      values,
      nextReadinessSort,
    ))
  }

  const fetchHistory = useCallback(async () => {
    setLoading(true)
    const values = form.getFieldsValue()

    try {
      const res = await apiFetch(buildPublishHistoryRequestUrl(appId, page, pageSize, values)) as PublishHistoryResponse
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
    const nextReadinessSort = normalizePublishHistoryReadinessSort(values.readinessSort)
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

  const getSortedEntries = () => {
    return sortPublishHistoryEntries(getPublishHistoryEntriesFromResponse(data), readinessSort)
  }

  const columns = buildPublishHistoryColumns({
    onOpenReview: setSelectedReviewJobId,
    onOpenReport: setSelectedReportJobId,
    onOpenArtifacts: setSelectedArtifactsJobId,
  })

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
