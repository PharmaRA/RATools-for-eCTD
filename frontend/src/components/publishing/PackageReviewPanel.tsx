import { useEffect, useState } from 'react'
import { Alert, Button, Card, Descriptions, Drawer, Space, Spin, Table, Tag, message } from 'antd'
import { CheckCircle, Download, XCircle } from 'lucide-react'

import { ApiRequestError, apiFetch } from '../../apiClient'
import { formatBytes } from '../../pages/appShared'

type PackageReviewPanelProps = {
  jobId: string | null
  onClose: () => void
}

type Artifact = {
  name: string
  exists?: boolean
  sizeBytes?: number
  contentType?: string
  type?: string
}

type ChecklistRow = {
  key: string
  check: string
  pass: boolean
  detail: string
}

type ChecklistExportRow = {
  key: string
  check: string
  status: 'Pass' | 'Fail'
  detail: string
}

const REQUIRED_ARTIFACTS = ['BackboneXml', 'PublishReport', 'PackageZip']

const isArtifact = (value: unknown): value is Artifact => {
  return !!value && typeof value === 'object' && typeof (value as Artifact).name === 'string'
}

const toArtifactArray = (value: unknown) => Array.isArray(value) ? value.filter(isArtifact) : []

const normalizeError = (error: unknown) => {
  if (error instanceof Error) return error
  return new Error(String(error))
}

const getReviewErrorTitle = (error: Error) => {
  if (!(error instanceof ApiRequestError)) return 'Unable to load package review data'

  switch (error.status) {
    case 404:
      return 'Report or artifacts were not found (404)'
    case 409:
      return 'Publish job is not ready (409)'
    case 410:
      return 'Publish data is unavailable (410)'
    case 422:
      return 'Publish report is corrupted (422)'
    default:
      return `Unable to load package review data (${error.status})`
  }
}

const getErrorMessage = (error: Error | null) => error?.message || ''

const hasArtifact = (artifacts: Artifact[], name: string) => artifacts.some((artifact) => artifact.name === name && artifact.exists === true)

const downloadJson = (filename: string, value: unknown) => {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export const PackageReviewPanel = ({ jobId, onClose }: PackageReviewPanelProps) => {
  const [loading, setLoading] = useState(false)
  const [report, setReport] = useState<any>(null)
  const [artifacts, setArtifacts] = useState<Artifact[]>([])
  const [artifactsLoaded, setArtifactsLoaded] = useState(false)
  const [reportError, setReportError] = useState<Error | null>(null)
  const [artifactsError, setArtifactsError] = useState<Error | null>(null)

  useEffect(() => {
    if (!jobId) {
      setLoading(false)
      setReport(null)
      setArtifacts([])
      setArtifactsLoaded(false)
      setReportError(null)
      setArtifactsError(null)
      return
    }

    let active = true

    setLoading(true)
    setReport(null)
    setArtifacts([])
    setArtifactsLoaded(false)
    setReportError(null)
    setArtifactsError(null)

    Promise.allSettled([
      apiFetch(`/api/publish-jobs/${jobId}/report`),
      apiFetch(`/api/publish-jobs/${jobId}/artifacts`),
    ]).then(([reportResult, artifactsResult]) => {
      if (!active) return

      if (reportResult.status === 'fulfilled') {
        setReport(reportResult.value)
      } else {
        setReportError(normalizeError(reportResult.reason))
      }

      if (artifactsResult.status === 'fulfilled') {
        const artifactData = artifactsResult.value
        setArtifacts(Array.isArray(artifactData) ? toArtifactArray(artifactData) : toArtifactArray(artifactData?.artifacts))
        setArtifactsLoaded(true)
      } else {
        setArtifactsError(normalizeError(artifactsResult.reason))
      }
    }).finally(() => {
      if (active) setLoading(false)
    })

    return () => {
      active = false
    }
  }, [jobId])

  const reportLoaded = !reportError && !!report
  const lifecycleMatches = report?.validationReport?.lifecycleMatches || []
  const lifecycleIssueCount = lifecycleMatches.filter((match: any) => match.resultCode !== 'MATCHED').length
  const presentArtifactCount = REQUIRED_ARTIFACTS.filter((name) => hasArtifact(artifacts, name)).length
  const packageZipExists = hasArtifact(artifacts, 'PackageZip')
  const publishReportExists = hasArtifact(artifacts, 'PublishReport')

  const checklistRows: ChecklistRow[] = [
    {
      key: 'publish-succeeded',
      check: 'Publish succeeded',
      pass: reportLoaded && report?.succeeded === true,
      detail: report?.message || reportError?.message || 'Report unavailable.',
    },
    {
      key: 'validation-errors',
      check: 'Validation errors',
      pass: reportLoaded && (report?.errorCount ?? 1) === 0,
      detail: reportLoaded ? `${report?.errorCount ?? '-'} error(s)` : 'Unavailable',
    },
    {
      key: 'lifecycle-issues',
      check: 'Lifecycle issues',
      pass: reportLoaded && lifecycleIssueCount === 0,
      detail: reportLoaded ? `${lifecycleIssueCount} issue(s)` : 'Unavailable',
    },
    {
      key: 'integrity-consistent',
      check: 'Integrity consistent',
      pass: reportLoaded && report?.integritySummary?.isConsistent === true,
      detail: report?.integritySummary?.isConsistent === true ? 'Consistent' : 'Inconsistent or unavailable',
    },
    {
      key: 'required-artifacts-present',
      check: 'Required artifacts present',
      pass: !artifactsError && presentArtifactCount === REQUIRED_ARTIFACTS.length,
      detail: artifactsError?.message || `${presentArtifactCount}/${REQUIRED_ARTIFACTS.length} present`,
    },
  ]

  const readyForSubmission = checklistRows.every((row) => row.pass)
  const findings = reportLoaded ? report?.integrityEvidence?.findings || [] : []
  const requiredArtifactRows = REQUIRED_ARTIFACTS.map((name) => artifacts.find((artifact) => artifact.name === name) || { name, exists: false })
  const reviewLoading = loading || (!!jobId && !report && !reportError && artifacts.length === 0 && !artifactsError)
  const riskSummaryItems = [
    { key: 'validation-errors', label: 'Validation Errors', children: report?.errorCount ?? '-' },
    { key: 'warnings', label: 'Warnings', children: report?.warningCount ?? '-' },
    { key: 'lifecycle-issues', label: 'Lifecycle Issues', children: reportLoaded ? lifecycleIssueCount : '-' },
    { key: 'missing-files', label: 'Missing Files', children: report?.integritySummary?.missingFilesCount ?? '-' },
    { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: report?.integritySummary?.missingZipEntriesCount ?? '-' },
    { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: report?.integritySummary?.mismatchedArtifactsCount ?? '-' },
  ]
  const reviewExportAvailable = reportLoaded || artifactsLoaded
  const checklistExportRows: ChecklistExportRow[] = checklistRows.map((row) => ({
    key: row.key,
    check: row.check,
    status: row.pass ? 'Pass' : 'Fail',
    detail: row.detail,
  }))

  const renderError = (error: Error | null) => error && (
    <Alert
      type="warning"
      showIcon
      title={getReviewErrorTitle(error)}
      description={getErrorMessage(error)}
    />
  )

  const handleDownloadReviewJson = () => {
    if (!jobId || !reviewExportAvailable) return

    try {
      const sequenceNumber = report?.sequenceNumber ?? null
      const exportObject = {
        reportVersion: 'package-review-export-v1',
        generatedAtUtc: new Date().toISOString(),
        publishJobId: jobId,
        sequenceNumber,
        validationProfile: report?.validationProfile ?? null,
        verdict: readyForSubmission ? 'ReadyForSubmission' : 'NotReadyForSubmission',
        checklist: checklistExportRows,
        riskSummary: {
          validationErrors: report?.errorCount ?? null,
          warnings: report?.warningCount ?? null,
          lifecycleIssues: reportLoaded ? lifecycleIssueCount : null,
          missingFiles: report?.integritySummary?.missingFilesCount ?? null,
          missingZipEntries: report?.integritySummary?.missingZipEntriesCount ?? null,
          mismatchedArtifacts: report?.integritySummary?.mismatchedArtifactsCount ?? null,
        },
        requiredArtifacts: requiredArtifactRows.map((artifact) => ({
          name: artifact.name,
          exists: artifact.exists === true,
          sizeBytes: artifact.sizeBytes,
          contentType: artifact.contentType,
        })),
        integrityFindings: findings,
      }

      downloadJson(`package-review-${sequenceNumber || 'unknown'}-${jobId}.json`, exportObject)
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
              Sequence {report?.sequenceNumber ?? '-'} | {report?.publishJob?.status ?? '-'} | {report?.validationProfile ?? '-'}
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

        {(report?.warningCount ?? 0) > 0 && (
          <Alert
            type="warning"
            showIcon
            title="Warnings do not block readiness"
            description={`${report.warningCount} warning(s) remain for reviewer awareness.`}
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
              { title: 'Status', dataIndex: 'pass', key: 'status', width: 120, render: (pass: boolean) => <Tag color={pass ? 'green' : 'red'}>{pass ? 'Pass' : 'Fail'}</Tag> },
              { title: 'Details', dataIndex: 'detail', key: 'detail' },
            ]}
          />
        </Card>

        <Card size="small" title="Risk Summary">
          <Descriptions bordered size="small" column={2} items={riskSummaryItems} />
        </Card>

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
              rowKey={(_: any, index) => `finding-${index}`}
              pagination={{ pageSize: 10 }}
              size="small"
              columns={[
                { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 100, render: (severity: string) => <Tag color={severity === 'Error' ? 'red' : 'orange'}>{severity}</Tag> },
                { title: 'Type', dataIndex: 'type', key: 'type', width: 180 },
                { title: 'Path', dataIndex: 'path', key: 'path', width: 260, render: (value?: string | null) => value || '-' },
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
              { title: 'Status', dataIndex: 'exists', key: 'status', render: (exists?: boolean) => exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag> },
              { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: (size?: number | null) => size == null ? '-' : formatBytes(size) },
              { title: 'Type', dataIndex: 'contentType', key: 'type', render: (value?: string) => value || '-' },
            ]}
          />
        </Card>
      </div>
      )}
    </Drawer>
  )
}
