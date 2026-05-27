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

  return executeRequest('/api/publish-jobs/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export type CreateAndExecutePublishJobRequest = ExecutePublishJobRequest

export const createAndExecutePublishJob = executePublishJob
