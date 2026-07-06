import { apiFetch, buildJsonRequestInit } from './apiClient'
import type { Application } from './pages/appShared'

export type CreateApplicationRequest = {
  applicationNumber: string
  ectdTemplateKey: string
  sponsorName: string
  workingDirectoryParentPath: string
}

export const buildApplicationsUrl = () => '/api/applications'

export const buildApplicationUrl = (applicationId: string) => `${buildApplicationsUrl()}/${applicationId}`

export const loadApplications = async (
  executeRequest: typeof apiFetch = apiFetch,
): Promise<Application[]> => {
  return executeRequest(buildApplicationsUrl())
}

export const createApplication = async (
  request: CreateApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  return executeRequest(buildApplicationsUrl(), buildJsonRequestInit('POST', request))
}
