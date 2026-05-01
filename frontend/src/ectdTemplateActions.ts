import { apiFetch } from './apiClient'
import { importApplication, type ImportApplicationRequest, type ImportApplicationResult } from './importActions'

export type EctdTemplateOption = {
  key: string
  displayName: string
  region: string
  standardName?: string
  standardVersion?: string
}

export type CreateApplicationRequest = {
  applicationNumber: string
  ectdTemplateKey: string
  sponsorName: string
  workingDirectoryParentPath: string
}

export const loadEctdTemplates = async (
  executeRequest: typeof apiFetch = apiFetch,
): Promise<EctdTemplateOption[]> => {
  return executeRequest('/api/ectd-templates')
}

export const getDefaultEctdTemplateKey = (templates: EctdTemplateOption[]) => templates[0]?.key

export const createApplication = async (
  request: CreateApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  return executeRequest('/api/applications', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
}

export const importApplicationWithTemplate = async (
  request: ImportApplicationRequest,
  executeRequest?: typeof apiFetch,
): Promise<ImportApplicationResult> => {
  return importApplication(request, executeRequest)
}
