import { useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Drawer, Row, Spin, Table, Tabs, Tag } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { formatBytes, formatDate, getLifecycleIssueCount } from '../../pages/appShared'

export const ReportPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false)
  const [errorState, setErrorState] = useState<{ status: number, message: string } | null>(null)
  const [report, setReport] = useState<any>(null)

  useEffect(() => {
    if (!jobId) return
    setLoading(true)
    setErrorState(null)
    setReport(null)
    apiFetch(`/api/publish-jobs/${jobId}/report`)
      .then((data) => setReport(data))
      .catch((err) => setErrorState(err))
      .finally(() => setLoading(false))
  }, [jobId])

  const renderError = () => {
    if (!errorState) return null
    let title = '无法加载报告'
    let type: 'error' | 'warning' | 'info' = 'error'
    if (errorState.status === 404) { title = '报告不存在 (404)'; type = 'warning' }
    if (errorState.status === 409) { title = '任务未完成 (409)'; type = 'info' }
    if (errorState.status === 410) { title = '报告文件已缺失 (410)'; type = 'warning' }
    if (errorState.status === 422) { title = '报告已损坏 (422)'; type = 'error' }
    return <Alert message={title} description={errorState.message} type={type} showIcon className="mt-4" />
  }

  const lifecycleIssueCount = getLifecycleIssueCount(report?.validationReport?.lifecycleSummary)
  const lifecycleMatches = report?.validationReport?.lifecycleMatches || []

  return (
    <Drawer title="Publish Report Details" placement="right" width={800} onClose={onClose} open={!!jobId}>
      {loading && <Spin className="w-full mt-10 flex justify-center" />}
      {renderError()}
      {report && (
        <div className="flex flex-col gap-4">
          <div className="flex justify-between items-center bg-gray-50 p-4 rounded">
            <div>
              <h2 className="text-lg font-bold flex items-center gap-2 m-0">
                {report.succeeded ? <CheckCircle className="text-green-500" /> : <XCircle className="text-red-500" />}
                {report.succeeded ? 'Publish Succeeded' : 'Publish Failed'}
              </h2>
              <p className="text-gray-500 m-0 text-sm mt-1">{report.message}</p>
            </div>
            <Button type="primary" icon={<Download size={16} className="mr-1" />} href={`/api/publish-jobs/${jobId}/artifacts/PublishReport/download`} target="_blank">
              Download JSON
            </Button>
          </div>
          <Descriptions bordered size="small" column={2}>
            <Descriptions.Item label="Profile">{report.validationProfile}</Descriptions.Item>
            <Descriptions.Item label="Duration">{report.durationMs} ms</Descriptions.Item>
            <Descriptions.Item label="Errors">{report.errorCount}</Descriptions.Item>
            <Descriptions.Item label="Warnings">{report.warningCount}</Descriptions.Item>
          </Descriptions>
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="Integrity Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="Consistent">{report.integritySummary ? (report.integritySummary.isConsistent ? 'Yes' : 'No') : '-'}</Descriptions.Item>
                  <Descriptions.Item label="Missing Files">{report.integritySummary?.missingFilesCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Missing Zip Entries">{report.integritySummary?.missingZipEntriesCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Mismatched Artifacts">{report.integritySummary?.mismatchedArtifactsCount ?? '-'}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="Artifact Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="File Count">{report.artifactSummary?.fileCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Total Size">{report.artifactSummary ? formatBytes(report.artifactSummary.totalSizeBytes || 0) : '-'}</Descriptions.Item>
                  <Descriptions.Item label="Package Size">{report.artifactSummary ? formatBytes(report.artifactSummary.packageSizeBytes || 0) : '-'}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="Audit Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="Publish Job Events">{report.auditSummary?.publishJobEventCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Validation Events">{report.auditSummary?.validationEventCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Latest Action">{report.auditSummary?.latestPublishJobAction ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Latest Event">{formatDate(report.auditSummary?.latestPublishJobEventUtc)}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="Lifecycle Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="Matched">{report.validationReport?.lifecycleSummary?.matchedCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Issues">{lifecycleIssueCount}</Descriptions.Item>
                  <Descriptions.Item label="Warning Summary">{report.warningSummary || '-'}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
          </Row>
          <Tabs defaultActiveKey="lifecycle">
            <Tabs.TabPane tab={`Lifecycle (${lifecycleMatches.length})`} key="lifecycle">
              <Table dataSource={lifecycleMatches} rowKey={(record: any, i) => `${record.documentId}-${i}`} pagination={{ pageSize: 10 }} size="small"
                columns={[
                  { title: 'Operation', dataIndex: 'operation', width: 120 },
                  { title: 'CTD Section', dataIndex: 'ctdSection', width: 120 },
                  { title: 'Result Code', dataIndex: 'resultCode', width: 240 },
                  { title: 'Match Strategy', dataIndex: 'matchStrategy', width: 180 },
                  { title: 'Historical Matches', dataIndex: 'historicalMatchCount', width: 140 },
                  { title: 'Historical Sequences', dataIndex: 'historicalSequenceNumbers', render: (values: string[]) => values?.join(', ') || '-' },
                ]}
              />
            </Tabs.TabPane>
            <Tabs.TabPane tab={`Validation Issues (${report.validationReport?.issues?.length || 0})`} key="issues">
              <Table dataSource={report.validationReport?.issues || []} rowKey={(_, i) => i + ''} pagination={{ pageSize: 10 }} size="small"
                columns={[
                  { title: 'Severity', dataIndex: 'severity', render: (s: string) => <Tag color={s === 'Error' ? 'red' : 'orange'}>{s}</Tag>, width: 100 },
                  { title: 'Code', dataIndex: 'code', width: 200 },
                  { title: 'Message', dataIndex: 'message' },
                ]}
              />
            </Tabs.TabPane>
          </Tabs>
        </div>
      )}
    </Drawer>
  )
}
