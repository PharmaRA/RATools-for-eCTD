import { apiFetch } from './apiClient'

export type CreateAndExecutePublishJobRequest = {
  applicationId: string
  sequenceNumber: string
  outputDirectoryPath: string
}

export const createAndExecutePublishJob = async (
  request: CreateAndExecutePublishJobRequest,
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
