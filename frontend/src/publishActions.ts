import { apiFetch, buildJsonRequestInit } from './apiClient'

export type ExecutePublishJobRequest = {
  applicationId: string
  sequenceNumber: string
  outputDirectoryPath: string
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
}

export const executePublishJob = async (
  request: ExecutePublishJobRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  const body = {
    applicationId: request.applicationId,
    sequenceNumber: request.sequenceNumber,
    outputDirectoryPath: request.outputDirectoryPath,
  }

  // 发布在后端后台执行：该端点返回 202 与作业（含 id/status），
  // 结果通过 History 标签页轮询作业状态与报告获取。
  return executeRequest('/api/publish-jobs/execute', buildJsonRequestInit('POST', body))
}

export type CreateAndExecutePublishJobRequest = ExecutePublishJobRequest

export const createAndExecutePublishJob = executePublishJob

export const buildPublishJobReportUrl = (jobId: string) => `/api/publish-jobs/${jobId}/report`

export const buildPublishJobArtifactsUrl = (jobId: string) => `/api/publish-jobs/${jobId}/artifacts`

export const buildPublishJobArtifactDownloadUrl = (
  jobId: string | null,
  artifactName: string,
) => `/api/publish-jobs/${jobId}/artifacts/${artifactName}/download`

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

  return `/api/applications/${appId}/publish-history?${params.toString()}`
}

export const loadPublishHistory = async <T = unknown>(
  request: LoadPublishHistoryRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<T> => {
  return executeRequest(buildPublishHistoryRequestUrl(
    request.applicationId,
    request.page,
    request.pageSize,
    request.filters,
  ))
}
