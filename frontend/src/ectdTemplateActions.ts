import { apiFetch } from './apiClient'
import type { EctdTemplateOption } from './api/contracts'
import { importApplication, type ImportApplicationRequest, type ImportApplicationResult } from './importActions'

export type { EctdTemplateOption } from './api/contracts'

export const buildEctdTemplatesUrl = () => '/api/ectd-templates'

export const loadEctdTemplates = async (
  executeRequest: typeof apiFetch = apiFetch,
): Promise<EctdTemplateOption[]> => {
  return executeRequest(buildEctdTemplatesUrl())
}

export const getDefaultEctdTemplateKey = (templates: EctdTemplateOption[]) => templates[0]?.key

export const buildEctdTemplateSelectOptions = (templates: readonly EctdTemplateOption[]) => templates.map((template) => ({
  value: template.key,
  label: template.displayName,
}))

export const importApplicationWithTemplate = async (
  request: ImportApplicationRequest,
  executeRequest?: typeof apiFetch,
): Promise<ImportApplicationResult> => {
  return importApplication(request, executeRequest)
}
