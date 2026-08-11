import { ApiRequestError, apiFetch, apiFetchResponse, buildJsonRequestInit } from './apiClient'
import { buildApplicationUrl } from './applicationActions'
import { downloadBlob, getDownloadFileName } from './browserDownload'
import { messages } from './i18n/messages'

export type ExecutePublishJobRequest = {
  applicationId: string
  sequenceNumber: string
}

export type PublishHistoryRequestFilterValues = {
  sequenceNumber?: string | null
  status?: string | null
  readinessStatus?: string | null
}

export type LoadPublishHistoryRequest = {
  applicationId: string
  page: number
  pageSize: number
  filters: PublishHistoryRequestFilterValues
  signal?: AbortSignal
}

export type ExecutedPublishJob = {
  id: string
  status: string
  failureReason?: string | null
  outputPath?: string | null
  packagePath?: string | null
}

export const executePublishJob = async (
  request: ExecutePublishJobRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ExecutedPublishJob> => {
  const body = {
    applicationId: request.applicationId,
    sequenceNumber: request.sequenceNumber,
  }

  // 发布在后端后台执行：该端点返回 202 与作业（含 id/status），
  // 调用方用返回的 id 轮询 GET /api/publish-jobs/{id} 获取进度与结果。
  return executeRequest(buildExecutePublishJobUrl(), buildJsonRequestInit('POST', body))
}

export type CreateAndExecutePublishJobRequest = ExecutePublishJobRequest

export const createAndExecutePublishJob = executePublishJob

export const buildPublishJobsUrl = () => '/api/publish-jobs'

export const buildExecutePublishJobUrl = () => `${buildPublishJobsUrl()}/execute`

export const buildPublishJobUrl = (jobId: string) => `${buildPublishJobsUrl()}/${jobId}`

export const buildPublishJobReportUrl = (jobId: string) => `${buildPublishJobUrl(jobId)}/report`

export const buildPublishJobArtifactsUrl = (jobId: string) => `${buildPublishJobUrl(jobId)}/artifacts`

export const buildPublishJobArtifactDownloadUrl = (
  jobId: string,
  artifactName: string,
) => `${buildPublishJobArtifactsUrl(jobId)}/${encodeURIComponent(artifactName)}/download`

const artifactFallbackFileNames: Record<string, string> = {
  BackboneXml: 'index.xml',
  PublishReport: 'publish-report.json',
  PackageZip: 'package.zip',
}

export const downloadArtifact = async (
  jobId: string,
  artifactName: string,
  executeRequest: typeof apiFetchResponse = apiFetchResponse,
  saveDownload: typeof downloadBlob = downloadBlob,
) => {
  const response = await executeRequest(buildPublishJobArtifactDownloadUrl(jobId, artifactName))
  const fallbackFileName = artifactFallbackFileNames[artifactName] || artifactName
  const fileName = getDownloadFileName(response.headers.get('content-disposition'), fallbackFileName)
  const blob = await response.blob()
  saveDownload(blob, fileName)
  return fileName
}

export const getArtifactDownloadErrorMessage = (error: unknown) => {
  if (error instanceof ApiRequestError) {
    if (error.status === 401) return messages.artifact.downloadUnauthorized
    if (error.status === 410) return messages.artifact.downloadGone
  }

  if (error instanceof TypeError) return messages.artifact.downloadNetworkError

  const detail = error instanceof Error && error.message
    ? error.message
    : messages.common.unknownError
  return `${messages.artifact.downloadFailed}：${detail}`
}

export const loadPublishJobReport = async <T = unknown>(
  jobId: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<T> => {
  return executeRequest(buildPublishJobReportUrl(jobId))
}

export const loadPublishJobArtifacts = async <T = unknown>(
  jobId: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<T> => {
  return executeRequest(buildPublishJobArtifactsUrl(jobId))
}

export const buildPublishHistoryRequestUrl = (
  appId: string,
  page: number,
  pageSize: number,
  values: PublishHistoryRequestFilterValues,
) => {
  const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
  if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber)
  if (values.status) params.append('status', values.status)
  if (values.readinessStatus) params.append('readinessStatus', values.readinessStatus)

  return `${buildApplicationUrl(appId)}/publish-history?${params.toString()}`
}

export const loadPublishHistory = async <T = unknown>(
  request: LoadPublishHistoryRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<T> => {
  const url = buildPublishHistoryRequestUrl(
    request.applicationId,
    request.page,
    request.pageSize,
    request.filters,
  )
  return request.signal
    ? executeRequest(url, { signal: request.signal })
    : executeRequest(url)
}
