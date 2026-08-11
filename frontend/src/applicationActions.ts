import { apiFetch, buildJsonRequestInit } from './apiClient'
import type { ApplicationContract, CreateApplicationContract } from './api/contracts'

export type CreateApplicationRequest = CreateApplicationContract

export const buildApplicationsUrl = () => '/api/applications'

export const buildApplicationUrl = (applicationId: string) => `${buildApplicationsUrl()}/${applicationId}`

export const loadApplications = async (
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ApplicationContract[]> => {
  return executeRequest(buildApplicationsUrl())
}

export const loadApplication = async (
  applicationId: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ApplicationContract> => {
  return executeRequest(buildApplicationUrl(applicationId))
}

export const createApplication = async (
  request: CreateApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  return executeRequest(buildApplicationsUrl(), buildJsonRequestInit('POST', request))
}
