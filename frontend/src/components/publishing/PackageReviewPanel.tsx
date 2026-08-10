import { useEffect, useState } from 'react'
import { Alert, Button, Card, Descriptions, Drawer, Space, Spin, Table, message } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { summarizeRequiredArtifacts } from '../../packageReviewSummary'
import { loadPublishJobArtifacts, loadPublishJobReport } from '../../publishActions'
import { getLifecycleMatches, summarizeLifecycleMatches } from '../../publishLifecycleSummary'
import { getArtifactsFromResponse } from './packageReviewArtifacts'
import { buildPackageReviewChecklistRows, isPackageReviewReadyForSubmission } from './packageReviewChecklist'
import {
  buildPackageReviewChecklistColumns,
  buildPackageReviewEvidenceFindingColumns,
  buildPackageReviewReadinessFindingColumns,
  buildPackageReviewRequiredArtifactColumns,
  buildPackageReviewRiskSummaryItems,
  formatPackageReviewHeaderSummary,
  formatPackageReviewWarningAlertDescription,
  getPackageReviewIntegrityFindings,
  getPackageReviewReadinessDisplayMeta,
} from './packageReviewDisplay'
import { downloadJson } from './packageReviewDownload'
import { ArtifactDownloadButton } from './ArtifactDownloadButton'
import { getReviewErrorDescription, getReviewErrorTitle, normalizePackageReviewError } from './packageReviewErrors'
import { buildPackageReviewPanelState } from './packageReviewPanelState'
import {
  buildPublishReadinessCategoryColumns,
  buildPublishReadinessSnapshotItems,
  getPublishReadinessCategoryKey,
  getPublishReadinessCategorySummaries,
  getPublishReadinessFindings,
  getPublishReadinessFindingKey,
  getPublishReadinessFromReport,
} from './publishReadinessDisplay'
import {
  buildPackageReviewExport,
  type PackageReviewArtifact,
  type PackageReviewReport,
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
        loadPublishJobReport<PackageReviewReport>(jobId),
        loadPublishJobArtifacts(jobId),
      ])

      if (!active) return

      if (reportResult.status === 'fulfilled') {
        setReport(reportResult.value)
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

  const {
    reportLoaded,
    reviewLoading,
    reviewExportAvailable,
  } = buildPackageReviewPanelState({
    jobId,
    loading,
    report,
    reportError,
    artifacts,
    artifactsLoaded,
    artifactsError,
  })
  const lifecycleMatches = getLifecycleMatches(report)
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

  const readyForSubmission = isPackageReviewReadyForSubmission(checklistRows)
  const findings = getPackageReviewIntegrityFindings(report, reportLoaded)
  const requiredArtifactRows = requiredArtifactSummary.rows
  const riskSummaryItems = buildPackageReviewRiskSummaryItems({ report, reportLoaded, lifecycleIssueCount })
  const publishReadiness = getPublishReadinessFromReport(report)
  const publishReadinessCategorySummaries = getPublishReadinessCategorySummaries(publishReadiness)
  const publishReadinessFindings = getPublishReadinessFindings(publishReadiness)
  const warningAlertDescription = formatPackageReviewWarningAlertDescription(report)
  const readinessDisplay = getPackageReviewReadinessDisplayMeta(readyForSubmission)

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
      message.error('导出包审阅失败。')
    }
  }

  return (
    <Drawer title="包审阅" placement="right" size={900} onClose={onClose} open={!!jobId}>
      {reviewLoading ? <Spin className="w-full mt-10 flex justify-center" /> : (
      <div className="flex flex-col gap-4">
        {renderError(reportError)}
        {renderError(artifactsError)}

        <div className="flex justify-between items-center bg-gray-50 p-4 rounded">
          <div>
            <h2 className="text-lg font-bold flex items-center gap-2 m-0">
              {readyForSubmission ? <CheckCircle className={readinessDisplay.iconClassName} /> : <XCircle className={readinessDisplay.iconClassName} />}
              {readinessDisplay.title}
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
              下载审阅 JSON
            </Button>
            <ArtifactDownloadButton
              type="primary"
              icon={<Download size={16} className="mr-1" />}
              jobId={jobId}
              artifactName="PackageZip"
              disabled={!packageZipExists}
            >
              下载包
            </ArtifactDownloadButton>
            <ArtifactDownloadButton
              icon={<Download size={16} className="mr-1" />}
              jobId={jobId}
              artifactName="PublishReport"
              disabled={!publishReportExists}
            >
              下载报告
            </ArtifactDownloadButton>
          </Space>
        </div>

        {warningAlertDescription && (
          <Alert
            type="warning"
            showIcon
            title="警告不影响就绪度"
            description={warningAlertDescription}
          />
        )}

        <Card size="small" title="提交就绪检查清单">
          <Table
            dataSource={checklistRows}
            rowKey="key"
            pagination={false}
            size="small"
            columns={buildPackageReviewChecklistColumns()}
          />
        </Card>

        <Card size="small" title="风险摘要">
          <Descriptions bordered size="small" column={2} items={riskSummaryItems} />
        </Card>

        {publishReadiness && (
          <Card size="small" title="发布就绪度快照">
            <div className="flex flex-col gap-3">
              <Descriptions
                bordered
                size="small"
                column={2}
                items={buildPublishReadinessSnapshotItems(publishReadiness)}
              />

              <Table
                dataSource={publishReadinessCategorySummaries}
                rowKey={getPublishReadinessCategoryKey}
                pagination={false}
                size="small"
                locale={{ emptyText: '未记录任何就绪度类别摘要。' }}
                columns={buildPublishReadinessCategoryColumns()}
              />

              <Table
                dataSource={publishReadinessFindings}
                rowKey={getPublishReadinessFindingKey}
                pagination={{ pageSize: 10 }}
                size="small"
                locale={{ emptyText: '未记录任何发布就绪度发现项。' }}
                columns={buildPackageReviewReadinessFindingColumns()}
              />
            </div>
          </Card>
        )}

        <Card size="small" title="证据预览">
          {!reportLoaded ? (
              <Alert
                type="warning"
                showIcon
                title="完整性证据不可用。"
                description="请加载发布报告以查看已记录的完整性证据。"
              />
          ) : findings.length > 0 ? (
            <Table
              dataSource={findings}
              rowKey={(_, index) => `finding-${index}`}
              pagination={{ pageSize: 10 }}
              size="small"
              columns={buildPackageReviewEvidenceFindingColumns()}
            />
          ) : (
            <Alert type="success" showIcon title="未记录任何完整性发现项。" />
          )}
        </Card>

        <Card size="small" title="必需产物">
          <Table
            dataSource={requiredArtifactRows}
            rowKey="name"
            pagination={false}
            size="small"
            columns={buildPackageReviewRequiredArtifactColumns()}
          />
        </Card>
      </div>
      )}
    </Drawer>
  )
}
