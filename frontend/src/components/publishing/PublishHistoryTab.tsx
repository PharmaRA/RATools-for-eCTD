import { useEffect, useState } from 'react'
import { Badge, Button, Card, Col, Form, Input, Row, Select, Space, Statistic, Table, message } from 'antd'

import { apiFetch } from '../../apiClient'
import { formatBytes, formatDate, getLifecycleIssueCount, getReportAvailabilityLabel, getStatusColor } from '../../pages/appShared'
import { ArtifactsPanel } from './ArtifactsPanel'
import { ReportPanel } from './ReportPanel'

export const PublishHistoryTab = ({ appId }: { appId: string }) => {
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<any>(null)
  const [selectedReportJobId, setSelectedReportJobId] = useState<string | null>(null)
  const [selectedArtifactsJobId, setSelectedArtifactsJobId] = useState<string | null>(null)
  const [form] = Form.useForm()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)

  const fetchHistory = () => {
    setLoading(true)
    const values = form.getFieldsValue()
    const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
    if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber)
    if (values.status) params.append('status', values.status)

    apiFetch(`/api/applications/${appId}/publish-history?${params.toString()}`)
      .then((res) => setData(res))
      .catch((err) => message.error('Failed to load history: ' + err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => { fetchHistory() }, [appId, page, pageSize])

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
      title: 'Actions', key: 'actions', fixed: 'right' as const, width: 200,
      render: (_: any, r: any) => (
        <Space>
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
        <Form form={form} layout="inline" onFinish={() => setPage(1)} className="mb-4">
          <Form.Item name="sequenceNumber" label="Sequence"><Input placeholder="e.g. 0000" allowClear className="w-32" /></Form.Item>
          <Form.Item name="status" label="Status">
            <Select placeholder="All" allowClear className="w-32">
              <Select.Option value="Completed">Completed</Select.Option>
              <Select.Option value="Failed">Failed</Select.Option>
              <Select.Option value="Running">Running</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item><Button type="primary" htmlType="submit">Filter</Button><Button className="ml-2" onClick={() => { form.resetFields(); setPage(1) }}>Reset</Button></Form.Item>
        </Form>
        <Table loading={loading} dataSource={data?.entries || []} columns={columns} rowKey="publishJobId" size="small"
          pagination={{ current: page, pageSize, total: data?.totalCount || 0, showSizeChanger: true, onChange: (p, ps) => { setPage(p); setPageSize(ps) } }}
        />
      </div>
      <ReportPanel jobId={selectedReportJobId} onClose={() => setSelectedReportJobId(null)} />
      <ArtifactsPanel jobId={selectedArtifactsJobId} onClose={() => setSelectedArtifactsJobId(null)} />
    </div>
  )
}
