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

  const jobRes = await executeRequest('/api/publish-jobs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  await executeRequest('/api/publish-jobs/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  return jobRes
}
