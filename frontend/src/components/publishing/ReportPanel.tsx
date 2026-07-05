import { useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Drawer, Row, Spin, Table, Tabs } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { summarizeLifecycleMatches } from '../../publishLifecycleSummary'
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
} from './reportDisplay'
import {
  buildPublishReadinessSnapshotItems,
  getPublishReadinessCategoryKey,
  getPublishReadinessFindingKey,
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
        const data = await apiFetch(`/api/publish-jobs/${jobId}/report`) as PublishReport
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

  const lifecycleMatches: ValidationLifecycleMatch[] = report?.validationReport?.lifecycleMatches || []
  const lifecycleSummary = summarizeLifecycleMatches(lifecycleMatches)
  const lifecycleIssueCount = lifecycleSummary.issueCount
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
          <Descriptions
            bordered
            size="small"
            column={2}
            items={buildReportOverviewItems(report, lifecycleIssueCount, integrityState)}
          />
          <Row gutter={16}>
            <Col span={12}>
              <Card size="small" title="Integrity Summary">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportIntegritySummaryItems(report.integritySummary, integrityState)}
                />
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="Artifact Summary">
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
              <Card size="small" title="Audit Summary">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportAuditSummaryItems(report.auditSummary)}
                />
              </Card>
            </Col>
            <Col span={12}>
              <Card size="small" title="Lifecycle Summary">
                <Descriptions
                  size="small"
                  column={1}
                  items={buildReportLifecycleSummaryItems(lifecycleSummary, report.warningSummary)}
                />
              </Card>
            </Col>
          </Row>
          {publishReadiness && (
            <Card size="small" title="Publish Readiness">
              <div className="flex flex-col gap-4">
                <Descriptions
                  bordered
                  size="small"
                  column={2}
                  items={buildPublishReadinessSnapshotItems(publishReadiness, { missingMetadataFieldsSpan: 2 })}
                />
                <Table
                  dataSource={publishReadiness.categorySummaries || []}
                  rowKey={getPublishReadinessCategoryKey}
                  pagination={false}
                  size="small"
                  locale={{ emptyText: 'No publish readiness category summaries were recorded.' }}
                  columns={buildReportPublishReadinessCategoryColumns()}
                />
                <Table
                  dataSource={publishReadiness.findings || []}
                  rowKey={getPublishReadinessFindingKey}
                  pagination={{ pageSize: 10 }}
                  size="small"
                  locale={{ emptyText: 'No publish readiness findings were recorded.' }}
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
                label: `Lifecycle (${lifecycleMatches.length})`,
                children: (
              <Table dataSource={lifecycleMatches} rowKey={(record, i) => `${record.documentId}-${i}`} pagination={{ pageSize: 10 }} size="small"
                columns={buildReportLifecycleMatchColumns()}
              />
                ),
              },
              {
                key: 'issues',
                label: `Validation Issues (${report.validationReport?.issues?.length || 0})`,
                children: (
              <Table dataSource={report.validationReport?.issues || []} rowKey={(_, i) => i + ''} pagination={{ pageSize: 10 }} size="small"
                columns={buildReportValidationIssueColumns()}
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
                        columns={buildReportIntegrityFindingColumns()}
                      />
                    </Card>
                    <Card size="small" title="Artifact Manifest">
                      <Table dataSource={report.integrityEvidence.artifacts || []} rowKey={(_, i) => `artifact-evidence-${i}`} pagination={{ pageSize: 10 }} size="small"
                        columns={buildReportArtifactManifestColumns()}
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
