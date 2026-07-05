import { useEffect, useState } from 'react'
import { Alert, Button, Card, Descriptions, Drawer, Space, Spin, Table, message } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { apiFetch } from '../../apiClient'
import { summarizeRequiredArtifacts } from '../../packageReviewSummary'
import { summarizeLifecycleMatches } from '../../publishLifecycleSummary'
import { formatOptionalBytes, formatOptionalText } from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { getArtifactsFromResponse } from './packageReviewArtifacts'
import { buildPackageReviewChecklistRows } from './packageReviewChecklist'
import {
  buildPackageReviewRiskSummaryItems,
  formatPackageReviewHeaderSummary,
  formatPackageReviewWarningAlertDescription,
  renderChecklistPassStatus,
  renderEvidenceFindingSeverityStatus,
  renderReadinessFindingSeverityStatus,
} from './packageReviewDisplay'
import { downloadJson } from './packageReviewDownload'
import { getReviewErrorDescription, getReviewErrorTitle, normalizePackageReviewError } from './packageReviewErrors'
import {
  formatReadinessCount,
  formatReadinessFieldName,
  formatMissingMetadataFields,
  formatReadinessReadyStatus,
  formatReadinessStatus,
  getPublishReadinessCategoryKey,
  getPublishReadinessFindingKey,
} from './publishReadinessDisplay'
import {
  buildPackageReviewExport,
  type IntegrityFinding,
  type PackageReviewArtifact,
  type PackageReviewReport,
  type PublishReadiness,
} from './packageReviewExport'

type PackageReviewPanelProps = {
  jobId: string | null
  onClose: () => void
}

const REQUIRED_ARTIFACTS = ['BackboneXml', 'PublishReport', 'PackageZip']

export const PackageReviewPanel = ({ jobId, onClose }: PackageReviewPanelProps) => {
  const [loading, setLoading] = useState(false)
  const [report, setReport] = useState<PackageReviewReport | null>(null)
  const [artifacts, setArtifacts] = useState<PackageReviewArtifact[]>([])
  const [artifactsLoaded, setArtifactsLoaded] = useState(false)
  const [reportError, setReportError] = useState<Error | null>(null)
  const [artifactsError, setArtifactsError] = useState<Error | null>(null)

  useEffect(() => {
    if (!jobId) {
      void Promise.resolve().then(() => {
        setLoading(false)
        setReport(null)
        setArtifacts([])
        setArtifactsLoaded(false)
        setReportError(null)
        setArtifactsError(null)
      })
      return
    }

    let active = true

    void Promise.resolve().then(async () => {
      setLoading(true)
      setReport(null)
      setArtifacts([])
      setArtifactsLoaded(false)
      setReportError(null)
      setArtifactsError(null)

      const [reportResult, artifactsResult] = await Promise.allSettled([
        apiFetch(`/api/publish-jobs/${jobId}/report`),
        apiFetch(`/api/publish-jobs/${jobId}/artifacts`),
      ])

      if (!active) return

      if (reportResult.status === 'fulfilled') {
        setReport(reportResult.value as PackageReviewReport)
      } else {
        setReportError(normalizePackageReviewError(reportResult.reason))
      }

      if (artifactsResult.status === 'fulfilled') {
        setArtifacts(getArtifactsFromResponse(artifactsResult.value))
        setArtifactsLoaded(true)
      } else {
        setArtifactsError(normalizePackageReviewError(artifactsResult.reason))
      }

      setLoading(false)
    })

    return () => {
      active = false
    }
  }, [jobId])

  const reportLoaded = !reportError && !!report
  const lifecycleMatches = report?.validationReport?.lifecycleMatches || []
  const lifecycleSummary = summarizeLifecycleMatches(lifecycleMatches)
  const lifecycleIssueCount = lifecycleSummary.issueCount
  const requiredArtifactSummary = summarizeRequiredArtifacts(artifacts, REQUIRED_ARTIFACTS)
  const presentArtifactCount = requiredArtifactSummary.presentCount
  const packageZipExists = requiredArtifactSummary.existsByName.PackageZip
  const publishReportExists = requiredArtifactSummary.existsByName.PublishReport

  const checklistRows = buildPackageReviewChecklistRows({
    report,
    reportLoaded,
    reportError,
    artifactsError,
    lifecycleIssueCount,
    presentArtifactCount,
    requiredArtifactCount: REQUIRED_ARTIFACTS.length,
  })

  const readyForSubmission = checklistRows.every((row) => row.pass)
  const findings: IntegrityFinding[] = reportLoaded ? report?.integrityEvidence?.findings || [] : []
  const requiredArtifactRows = requiredArtifactSummary.rows
  const reviewLoading = loading || (!!jobId && !report && !reportError && artifacts.length === 0 && !artifactsError)
  const riskSummaryItems = buildPackageReviewRiskSummaryItems({ report, reportLoaded, lifecycleIssueCount })
  const reviewExportAvailable = reportLoaded || artifactsLoaded
  const publishReadiness = (report?.publishReadiness || null) as PublishReadiness | null
  const warningAlertDescription = formatPackageReviewWarningAlertDescription(report)

  const renderError = (error: Error | null) => error && (
    <Alert
      type="warning"
      showIcon
      title={getReviewErrorTitle(error)}
      description={getReviewErrorDescription(error)}
    />
  )

  const handleDownloadReviewJson = () => {
    if (!jobId || !reviewExportAvailable) return

    try {
      const exportReview = buildPackageReviewExport({
        jobId,
        generatedAtUtc: new Date().toISOString(),
        report,
        reportLoaded,
        readyForSubmission,
        lifecycleIssueCount,
        reportError,
        artifactsError,
        checklistRows,
        requiredArtifactRows,
        integrityFindings: findings,
      })

      downloadJson(exportReview.filename, exportReview.value)
    } catch {
      message.error('Failed to export package review.')
    }
  }

  return (
    <Drawer title="Package Review" placement="right" size={900} onClose={onClose} open={!!jobId}>
      {reviewLoading ? <Spin className="w-full mt-10 flex justify-center" /> : (
      <div className="flex flex-col gap-4">
        {renderError(reportError)}
        {renderError(artifactsError)}

        <div className="flex justify-between items-center bg-gray-50 p-4 rounded">
          <div>
            <h2 className="text-lg font-bold flex items-center gap-2 m-0">
              {readyForSubmission ? <CheckCircle className="text-green-500" /> : <XCircle className="text-red-500" />}
              {readyForSubmission ? 'Ready for Submission' : 'Not Ready for Submission'}
            </h2>
            <p className="text-gray-500 m-0 text-sm mt-1">
              {formatPackageReviewHeaderSummary(report)}
            </p>
          </div>
          <Space>
            <Button
              icon={<Download size={16} className="mr-1" />}
              onClick={handleDownloadReviewJson}
              disabled={!reviewExportAvailable}
            >
              Download Review JSON
            </Button>
            <Button
              type="primary"
              icon={<Download size={16} className="mr-1" />}
              href={`/api/publish-jobs/${jobId}/artifacts/PackageZip/download`}
              target="_blank"
              disabled={!packageZipExists}
            >
              Download Package
            </Button>
            <Button
              icon={<Download size={16} className="mr-1" />}
              href={`/api/publish-jobs/${jobId}/artifacts/PublishReport/download`}
              target="_blank"
              disabled={!publishReportExists}
            >
              Download Report
            </Button>
          </Space>
        </div>

        {warningAlertDescription && (
          <Alert
            type="warning"
            showIcon
            title="Warnings do not block readiness"
            description={warningAlertDescription}
          />
        )}

        <Card size="small" title="Submission Readiness Checklist">
          <Table
            dataSource={checklistRows}
            rowKey="key"
            pagination={false}
            size="small"
            columns={[
              { title: 'Check', dataIndex: 'check', key: 'check' },
              { title: 'Status', dataIndex: 'pass', key: 'status', width: 120, render: renderChecklistPassStatus },
              { title: 'Details', dataIndex: 'detail', key: 'detail' },
            ]}
          />
        </Card>

        <Card size="small" title="Risk Summary">
          <Descriptions bordered size="small" column={2} items={riskSummaryItems} />
        </Card>

        {publishReadiness && (
          <Card size="small" title="Publish Readiness Snapshot">
            <div className="flex flex-col gap-3">
              <Descriptions
                bordered
                size="small"
                column={2}
                items={[
                  { key: 'readiness-status', label: 'Status', children: formatReadinessStatus(publishReadiness.status) },
                  { key: 'readiness-ready', label: 'Ready', children: formatReadinessReadyStatus(publishReadiness.isReady) },
                  { key: 'readiness-blocking-errors', label: 'Blocking Errors', children: formatReadinessCount(publishReadiness.blockingErrorCount) },
                  { key: 'readiness-warnings', label: 'Warnings', children: formatReadinessCount(publishReadiness.warningCount) },
                  {
                    key: 'readiness-missing-fields',
                    label: 'Missing Metadata Fields',
                    children: formatMissingMetadataFields(publishReadiness.missingMetadataFields),
                  },
                ]}
              />

              <Table
                dataSource={publishReadiness.categorySummaries || []}
                rowKey={getPublishReadinessCategoryKey}
                pagination={false}
                size="small"
                locale={{ emptyText: 'No readiness category summaries were recorded.' }}
                columns={[
                  { title: 'Category', dataIndex: 'category', key: 'category' },
                  { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', key: 'blockingErrorCount', width: 140 },
                  { title: 'Warnings', dataIndex: 'warningCount', key: 'warningCount', width: 120 },
                  { title: 'Findings', dataIndex: 'findingCount', key: 'findingCount', width: 120 },
                ]}
              />

              <Table
                dataSource={publishReadiness.findings || []}
                rowKey={getPublishReadinessFindingKey}
                pagination={{ pageSize: 10 }}
                size="small"
                locale={{ emptyText: 'No publish readiness findings were recorded.' }}
                columns={[
                  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 120, render: renderReadinessFindingSeverityStatus },
                  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
                  { title: 'Category', dataIndex: 'category', key: 'category', width: 180 },
                  { title: 'Field', dataIndex: 'fieldName', key: 'fieldName', width: 180, render: formatReadinessFieldName },
                  { title: 'Recommended Action', dataIndex: 'recommendedAction', key: 'recommendedAction' },
                ]}
              />
            </div>
          </Card>
        )}

        <Card size="small" title="Evidence Preview">
          {!reportLoaded ? (
              <Alert
                type="warning"
                showIcon
                title="Integrity evidence unavailable."
                description="Load the publish report to review recorded integrity evidence."
              />
          ) : findings.length > 0 ? (
            <Table
              dataSource={findings}
              rowKey={(_, index) => `finding-${index}`}
              pagination={{ pageSize: 10 }}
              size="small"
              columns={[
                { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 100, render: renderEvidenceFindingSeverityStatus },
                { title: 'Type', dataIndex: 'type', key: 'type', width: 180 },
                { title: 'Path', dataIndex: 'path', key: 'path', width: 260, render: formatOptionalText },
                { title: 'Message', dataIndex: 'message', key: 'message' },
              ]}
            />
          ) : (
            <Alert type="success" showIcon title="No integrity findings were recorded." />
          )}
        </Card>

        <Card size="small" title="Required Artifacts">
          <Table
            dataSource={requiredArtifactRows}
            rowKey="name"
            pagination={false}
            size="small"
            columns={[
              { title: 'Name', dataIndex: 'name', key: 'name', render: (name: string) => <b>{name}</b> },
              { title: 'Status', dataIndex: 'exists', key: 'status', render: renderArtifactExistsStatus },
              { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: formatOptionalBytes },
              { title: 'Type', dataIndex: 'contentType', key: 'type', render: formatOptionalText },
            ]}
          />
        </Card>
      </div>
      )}
    </Drawer>
  )
}
