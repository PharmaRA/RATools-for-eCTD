import { apiFetch } from './apiClient'

export type ExecutePublishJobRequest = {
  applicationId: string
  sequenceNumber: string
  outputDirectoryPath: string
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
  return executeRequest('/api/publish-jobs/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export type CreateAndExecutePublishJobRequest = ExecutePublishJobRequest

export const createAndExecutePublishJob = executePublishJob
