import { useEffect, useState } from 'react'
import { Alert, Card, Col, Descriptions, Drawer, Row, Spin, Table, Tabs } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { loadPublishJobReport } from '../../publishActions'
import { getLifecycleMatches, summarizeLifecycleMatches } from '../../publishLifecycleSummary'
import {
  getReportErrorAlertMeta,
  toReportErrorState,
  type ReportErrorState,
} from './reportErrors'
import {
  buildReportArtifactManifestColumns,
  buildReportArtifactSummaryItems,
  buildReportAuditSummaryItems,
  buildReportIntegrityFindingColumns,
  buildReportIntegritySummaryItems,
  buildReportLifecycleMatchColumns,
  buildReportLifecycleSummaryItems,
  buildReportOverviewItems,
  buildReportPublishReadinessCategoryColumns,
  buildReportPublishReadinessFindingColumns,
  buildReportValidationIssueColumns,
  formatReportIntegrityState,
  getReportIntegrityArtifacts,
  getReportIntegrityFindings,
  getReportValidationIssues,
  getReportOutcomeDisplayMeta,
} from './reportDisplay'
import { ArtifactDownloadButton } from './ArtifactDownloadButton'
import {
  buildPublishReadinessSnapshotItems,
  getPublishReadinessCategoryKey,
  getPublishReadinessCategorySummaries,
  getPublishReadinessFindings,
  getPublishReadinessFindingKey,
  getPublishReadinessFromReport,
} from './publishReadinessDisplay'

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

export const ReportPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false)
  const [errorState, setErrorState] = useState<ReportErrorState | null>(null)
  const [report, setReport] = useState<PublishReport | null>(null)

  useEffect(() => {
    if (!jobId) return

    let active = true

    void Promise.resolve().then(async () => {
      setLoading(true)
      setErrorState(null)
      setReport(null)
      try {
        const data = await loadPublishJobReport<PublishReport>(jobId)
        if (active) setReport(data)
      } catch (err) {
        if (active) setErrorState(toReportErrorState(err))
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
    const { title, type } = getReportErrorAlertMeta(errorState.status)
    return <Alert title={title} description={errorState.message} type={type} showIcon className="mt-4" />
  }

  const lifecycleMatches: ValidationLifecycleMatch[] = getLifecycleMatches(report)
  const lifecycleSummary = summarizeLifecycleMatches(lifecycleMatches)
  const lifecycleIssueCount = lifecycleSummary.issueCount
  const integrityState = formatReportIntegrityState(report?.integritySummary)
  const publishReadiness = getPublishReadinessFromReport(report)
  const publishReadinessCategorySummaries = getPublishReadinessCategorySummaries(publishReadiness)
  const publishReadinessFindings = getPublishReadinessFindings(publishReadiness)
  const validationIssues = getReportValidationIssues(report)
  const integrityFindings = getReportIntegrityFindings(report)
  const integrityArtifacts = getReportIntegrityArtifacts(report)
  const reportOutcome = getReportOutcomeDisplayMeta(report?.succeeded)

  return (
    <Drawer title="发布报告详情" placement="right" size={800} onClose={onClose} open={!!jobId}>
      {loading && <Spin className="w-full mt-10 flex justify-center" />}
      {renderError()}
      {report && (
        <div className="flex flex-col gap-4">
          <div className="flex justify-between items-center bg-gray-50 p-4 rounded">
            <div>
              <h2 className="text-lg font-bold flex items-center gap-2 m-0">
                {report.succeeded ? <CheckCircle className={reportOutcome.iconClassName} /> : <XCircle className={reportOutcome.iconClassName} />}
                {reportOutcome.title}
              </h2>
              <p className="text-gray-500 m-0 text-sm mt-1">{report.message}</p>
            </div>
            <ArtifactDownloadButton
              type="primary"
              icon={<Download size={16} className="mr-1" />}
              jobId={jobId}
              artifactName="PublishReport"
            >
              下载 JSON
            </ArtifactDownloadButton>
          </div>
          <Descriptions
            bordered
            size="small"
            column={2}
            items={buildReportOverviewItems(report, lifecycleIssueCount, integrityState)}
          />
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="完整性摘要">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportIntegritySummaryItems(report.integritySummary, integrityState)}
                />
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="产物摘要">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportArtifactSummaryItems(report.artifactSummary)}
                />
              </Card>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="审计摘要">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportAuditSummaryItems(report.auditSummary)}
                />
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="生命周期摘要">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportLifecycleSummaryItems(lifecycleSummary, report.warningSummary)}
                />
              </Card>
            </Col>
          </Row>
          {publishReadiness && (
            <Card size="small" title="发布就绪度">
              <div className="flex flex-col gap-4">
                <Descriptions
                  bordered
                  size="small"
                  column={2}
                  items={buildPublishReadinessSnapshotItems(publishReadiness, { missingMetadataFieldsSpan: 2 })}
                />
                <Table
                  dataSource={publishReadinessCategorySummaries}
                  rowKey={getPublishReadinessCategoryKey}
                  pagination={false}
                  size="small"
                  locale={{ emptyText: '未记录任何发布就绪度类别摘要。' }}
                  columns={buildReportPublishReadinessCategoryColumns()}
                />
                <Table
                  dataSource={publishReadinessFindings}
                  rowKey={getPublishReadinessFindingKey}
                  pagination={{ pageSize: 10 }}
                  size="small"
                  locale={{ emptyText: '未记录任何发布就绪度发现项。' }}
                  columns={buildReportPublishReadinessFindingColumns()}
                />
              </div>
            </Card>
          )}
          <Tabs
            defaultActiveKey="lifecycle"
            items={[
              {
                key: 'lifecycle',
                label: `生命周期 (${lifecycleMatches.length})`,
                children: (
              <Table dataSource={lifecycleMatches} rowKey={(record, i) => `${record.documentId}-${i}`} pagination={{ pageSize: 10 }} size="small"
                columns={buildReportLifecycleMatchColumns()}
              />
                ),
              },
              {
                key: 'issues',
                label: `校验问题 (${validationIssues.length})`,
                children: (
                  <Table
                    dataSource={validationIssues}
                    rowKey={(_, i) => i + ''}
                    pagination={{ pageSize: 10 }}
                    size="small"
                    columns={buildReportValidationIssueColumns()}
                  />
                ),
              },
              {
                key: 'evidence',
                label: '证据',
                children: report.integrityEvidence ? (
                  <div className="flex flex-col gap-4">
                    <Card size="small" title="完整性发现">
                      <Table dataSource={integrityFindings} rowKey={(_, i) => `finding-${i}`} pagination={{ pageSize: 10 }} size="small"
                        locale={{ emptyText: '未记录任何完整性发现项。' }}
                        columns={buildReportIntegrityFindingColumns()}
                      />
                    </Card>
                    <Card size="small" title="产物清单">
                      <Table dataSource={integrityArtifacts} rowKey={(_, i) => `artifact-evidence-${i}`} pagination={{ pageSize: 10 }} size="small"
                        columns={buildReportArtifactManifestColumns()}
                      />
                    </Card>
                  </div>
                ) : (
                  <Alert type="info" showIcon title="未记录该报告的详细完整性证据。" />
                ),
              },
            ]}
          />
        </div>
      )}
    </Drawer>
  )
}
