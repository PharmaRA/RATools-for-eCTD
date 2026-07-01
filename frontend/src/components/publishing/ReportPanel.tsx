import { useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Drawer, Row, Spin, Table, Tabs, Tag } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { formatBytes, formatDate, getErrorMessage } from '../../pages/appShared'

type ValidationLifecycleMatch = {
  operation?: string | null
  sequenceNumber?: string | null
  ctdSection?: string | null
  documentId?: string | null
  resultCode: string
  matchStrategy?: string | null
  attemptedStrategies?: string[]
  historicalMatchCount?: number | null
  historicalSequenceNumbers?: string[]
  historicalPlacementIds?: string[]
  historicalFinalState?: string | null
}

type ValidationIssue = {
  severity: string
  code: string
  message: string
}

type IntegrityFinding = {
  severity: string
  type: string
  path?: string | null
  message: string
}

type ArtifactEvidence = {
  role: string
  relativePath?: string | null
  exists?: boolean
  sizeBytes?: number | null
  zipEntryPresent?: boolean | null
  source?: string | null
}

type PublishReport = {
  succeeded?: boolean
  message?: string
  validationProfile?: string
  durationMs?: number
  errorCount?: number
  warningCount?: number
  warningSummary?: string | null
  validationReport?: {
    issues?: ValidationIssue[]
    lifecycleMatches?: ValidationLifecycleMatch[]
  }
  integritySummary?: {
    isConsistent?: boolean
    missingFilesCount?: number
    missingZipEntriesCount?: number
    mismatchedArtifactsCount?: number
  }
  artifactSummary?: {
    fileCount?: number
    totalSizeBytes?: number
    packageSizeBytes?: number
  }
  auditSummary?: {
    publishJobEventCount?: number
    validationEventCount?: number
    latestPublishJobAction?: string | null
    latestPublishJobEventUtc?: string | null
  }
  integrityEvidence?: {
    findings?: IntegrityFinding[]
    artifacts?: ArtifactEvidence[]
  }
  publishReadiness?: PublishReadiness | null
}

const buildLifecycleSummary = (matches: ValidationLifecycleMatch[]) => ({
  matchedCount: matches.filter((match) => match.resultCode === 'MATCHED').length,
  replaceTargetNotFoundCount: matches.filter((match) => match.resultCode === 'REPLACE_TARGET_NOT_FOUND').length,
  deleteTargetNotFoundCount: matches.filter((match) => match.resultCode === 'DELETE_TARGET_NOT_FOUND').length,
  appendTargetNotFoundCount: matches.filter((match) => match.resultCode === 'APPEND_TARGET_NOT_FOUND').length,
  ambiguousCount: matches.filter((match) => match.resultCode === 'LIFECYCLE_TARGET_AMBIGUOUS').length,
  currentSequenceCount: matches.filter((match) => match.resultCode === 'LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE').length,
})

const getLifecycleMatchIssueCount = (matches: ValidationLifecycleMatch[]) => matches.filter((match) => match.resultCode !== 'MATCHED').length

const formatList = (values?: unknown[]) => values?.length ? values.join(', ') : '-'

const formatBooleanStatus = (value?: boolean | null) => {
  if (value === true) return <Tag color="green">Present</Tag>
  if (value === false) return <Tag color="red">Missing from zip</Tag>
  return '-'
}

const formatExistsStatus = (exists?: boolean) => exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag>

type PublishReadiness = {
  isReady?: boolean
  status?: string
  blockingErrorCount?: number
  warningCount?: number
  missingMetadataFields?: string[]
  categorySummaries?: Array<{
    category: string
    blockingErrorCount: number
    warningCount: number
    findingCount: number
  }>
  findings?: Array<{
    severity: string
    code: string
    category: string
    fieldName?: string | null
    recommendedAction: string
  }>
}

const toReportError = (error: unknown) => {
  const status = typeof (error as { status?: unknown })?.status === 'number'
    ? (error as { status: number }).status
    : 0

  return { status, message: getErrorMessage(error) }
}

export const ReportPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false)
  const [errorState, setErrorState] = useState<{ status: number, message: string } | null>(null)
  const [report, setReport] = useState<PublishReport | null>(null)

  useEffect(() => {
    if (!jobId) return

    let active = true

    void Promise.resolve().then(async () => {
      setLoading(true)
      setErrorState(null)
      setReport(null)
      try {
        const data = await apiFetch(`/api/publish-jobs/${jobId}/report`) as PublishReport
        if (active) setReport(data)
      } catch (err) {
        if (active) setErrorState(toReportError(err))
      } finally {
        if (active) setLoading(false)
      }
    })

    return () => {
      active = false
    }
  }, [jobId])

  const renderError = () => {
    if (!errorState) return null
    let title = '无法加载报告'
    let type: 'error' | 'warning' | 'info' = 'error'
    if (errorState.status === 404) { title = '报告不存在 (404)'; type = 'warning' }
    if (errorState.status === 409) { title = '任务未完成 (409)'; type = 'info' }
    if (errorState.status === 410) { title = '报告文件已缺失 (410)'; type = 'warning' }
    if (errorState.status === 422) { title = '报告已损坏 (422)'; type = 'error' }
    return <Alert title={title} description={errorState.message} type={type} showIcon className="mt-4" />
  }

  const lifecycleMatches: ValidationLifecycleMatch[] = report?.validationReport?.lifecycleMatches || []
  const lifecycleSummary = buildLifecycleSummary(lifecycleMatches)
  const lifecycleIssueCount = getLifecycleMatchIssueCount(lifecycleMatches)
  const integrityState = report?.integritySummary ? (report.integritySummary.isConsistent ? 'Consistent' : 'Inconsistent') : '-'
  const publishReadiness = (report?.publishReadiness || null) as PublishReadiness | null

  return (
    <Drawer title="Publish Report Details" placement="right" size={800} onClose={onClose} open={!!jobId}>
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
            <Descriptions.Item label="Lifecycle Issues">{lifecycleIssueCount}</Descriptions.Item>
            <Descriptions.Item label="Integrity">{integrityState}</Descriptions.Item>
          </Descriptions>
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="Integrity Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="Consistent">{integrityState}</Descriptions.Item>
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
                  <Descriptions.Item label="Latest Event">{formatDate(report.auditSummary?.latestPublishJobEventUtc ?? undefined)}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="Lifecycle Summary">
                <Descriptions size="small" column={1}>
                  <Descriptions.Item label="Matched">{lifecycleSummary.matchedCount}</Descriptions.Item>
                  <Descriptions.Item label="Issues">{lifecycleIssueCount}</Descriptions.Item>
                  <Descriptions.Item label="Replace Missing">{lifecycleSummary.replaceTargetNotFoundCount}</Descriptions.Item>
                  <Descriptions.Item label="Delete Missing">{lifecycleSummary.deleteTargetNotFoundCount}</Descriptions.Item>
                  <Descriptions.Item label="Append Missing">{lifecycleSummary.appendTargetNotFoundCount}</Descriptions.Item>
                  <Descriptions.Item label="Ambiguous">{lifecycleSummary.ambiguousCount}</Descriptions.Item>
                  <Descriptions.Item label="Current Sequence">{lifecycleSummary.currentSequenceCount}</Descriptions.Item>
                  <Descriptions.Item label="Warning Summary">{report.warningSummary || '-'}</Descriptions.Item>
                </Descriptions>
              </Card>
            </Col>
          </Row>
          {publishReadiness && (
            <Card size="small" title="Publish Readiness">
              <div className="flex flex-col gap-4">
                <Descriptions bordered size="small" column={2}>
                  <Descriptions.Item label="Status">{publishReadiness.status || '-'}</Descriptions.Item>
                  <Descriptions.Item label="Ready">{publishReadiness.isReady ? 'Yes' : 'No'}</Descriptions.Item>
                  <Descriptions.Item label="Blocking Errors">{publishReadiness.blockingErrorCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Warnings">{publishReadiness.warningCount ?? '-'}</Descriptions.Item>
                  <Descriptions.Item label="Missing Metadata Fields" span={2}>
                    {(publishReadiness.missingMetadataFields || []).length > 0
                      ? publishReadiness.missingMetadataFields!.join(', ')
                      : 'None'}
                  </Descriptions.Item>
                </Descriptions>
                <Table
                  dataSource={publishReadiness.categorySummaries || []}
                  rowKey={(row) => row.category}
                  pagination={false}
                  size="small"
                  locale={{ emptyText: 'No publish readiness category summaries were recorded.' }}
                  columns={[
                    { title: 'Category', dataIndex: 'category', width: 220 },
                    { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', width: 140 },
                    { title: 'Warnings', dataIndex: 'warningCount', width: 120 },
                    { title: 'Findings', dataIndex: 'findingCount', width: 120 },
                  ]}
                />
                <Table
                  dataSource={publishReadiness.findings || []}
                  rowKey={(row, index) => `${row.code}-${row.fieldName || 'none'}-${index}`}
                  pagination={{ pageSize: 10 }}
                  size="small"
                  locale={{ emptyText: 'No publish readiness findings were recorded.' }}
                  columns={[
                    { title: 'Severity', dataIndex: 'severity', width: 100, render: (value: string) => <Tag color={value === 'Error' ? 'red' : 'orange'}>{value}</Tag> },
                    { title: 'Code', dataIndex: 'code', width: 220 },
                    { title: 'Category', dataIndex: 'category', width: 180 },
                    { title: 'Field', dataIndex: 'fieldName', width: 180, render: (value?: string | null) => value || '-' },
                    { title: 'Recommended Action', dataIndex: 'recommendedAction' },
                  ]}
                />
              </div>
            </Card>
          )}
          <Tabs
            defaultActiveKey="lifecycle"
            items={[
              {
                key: 'lifecycle',
                label: `Lifecycle (${lifecycleMatches.length})`,
                children: (
              <Table dataSource={lifecycleMatches} rowKey={(record, i) => `${record.documentId}-${i}`} pagination={{ pageSize: 10 }} size="small"
                columns={[
                  { title: 'Operation', dataIndex: 'operation', width: 120 },
                  { title: 'Sequence', dataIndex: 'sequenceNumber', width: 100 },
                  { title: 'CTD Section', dataIndex: 'ctdSection', width: 120 },
                  { title: 'Document ID', dataIndex: 'documentId', width: 180 },
                  { title: 'Result Code', dataIndex: 'resultCode', width: 240 },
                  { title: 'Match Strategy', dataIndex: 'matchStrategy', width: 180 },
                  { title: 'Attempted Strategies', dataIndex: 'attemptedStrategies', render: formatList, width: 220 },
                  { title: 'Historical Matches', dataIndex: 'historicalMatchCount', width: 140 },
                  { title: 'Historical Sequences', dataIndex: 'historicalSequenceNumbers', render: formatList, width: 180 },
                  { title: 'Historical Placement IDs', dataIndex: 'historicalPlacementIds', render: formatList, width: 240 },
                  { title: 'Final State', dataIndex: 'historicalFinalState', width: 140 },
                ]}
              />
                ),
              },
              {
                key: 'issues',
                label: `Validation Issues (${report.validationReport?.issues?.length || 0})`,
                children: (
              <Table dataSource={report.validationReport?.issues || []} rowKey={(_, i) => i + ''} pagination={{ pageSize: 10 }} size="small"
                columns={[
                  { title: 'Severity', dataIndex: 'severity', render: (s: string) => <Tag color={s === 'Error' ? 'red' : 'orange'}>{s}</Tag>, width: 100 },
                  { title: 'Code', dataIndex: 'code', width: 200 },
                  { title: 'Message', dataIndex: 'message' },
                ]}
              />
                ),
              },
              {
                key: 'evidence',
                label: 'Evidence',
                children: report.integrityEvidence ? (
                  <div className="flex flex-col gap-4">
                    <Card size="small" title="Integrity Findings">
                      <Table dataSource={report.integrityEvidence.findings || []} rowKey={(_, i) => `finding-${i}`} pagination={{ pageSize: 10 }} size="small"
                        locale={{ emptyText: 'No integrity findings were recorded.' }}
                        columns={[
                          { title: 'Severity', dataIndex: 'severity', width: 100, render: (value: string) => <Tag color={value === 'Error' ? 'red' : 'orange'}>{value}</Tag> },
                          { title: 'Type', dataIndex: 'type', width: 200 },
                          { title: 'Path', dataIndex: 'path', width: 260, render: (value?: string | null) => value || '-' },
                          { title: 'Message', dataIndex: 'message' },
                        ]}
                      />
                    </Card>
                    <Card size="small" title="Artifact Manifest">
                      <Table dataSource={report.integrityEvidence.artifacts || []} rowKey={(_, i) => `artifact-evidence-${i}`} pagination={{ pageSize: 10 }} size="small"
                        columns={[
                          { title: 'Role', dataIndex: 'role', width: 140 },
                          { title: 'Relative Path', dataIndex: 'relativePath', width: 260, render: (value?: string | null) => value || '-' },
                          { title: 'Exists', dataIndex: 'exists', width: 120, render: formatExistsStatus },
                          { title: 'Size', dataIndex: 'sizeBytes', width: 120, render: (value: number) => formatBytes(value || 0) },
                          { title: 'Zip Entry', dataIndex: 'zipEntryPresent', width: 150, render: formatBooleanStatus },
                          { title: 'Source', dataIndex: 'source', width: 160 },
                        ]}
                      />
                    </Card>
                  </div>
                ) : (
                  <Alert type="info" showIcon title="No detailed integrity evidence was recorded for this report." />
                ),
              },
            ]}
          />
        </div>
      )}
    </Drawer>
  )
}
