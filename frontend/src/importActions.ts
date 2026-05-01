import { ApiRequestError, apiFetch } from './apiClient'

export type ImportApplicationRequest = {
  workingDirectoryPath: string
  ectdTemplateKey: string
  sponsorName: string
}

export type ImportApplicationIssue = {
  severity: string
  code: string
  sequenceNumber: string | null
  message: string
}

export type ImportApplicationResult = {
  applicationId: string
  applicationNumber: string
  workingDirectoryPath: string
  importedSequenceCount: number
  importedDocumentCount: number
  importedPlacementCount: number
  skippedSequenceCount: number
  failedSequenceCount: number
  issues: ImportApplicationIssue[]
}

export const importApplication = async (
  request: ImportApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ImportApplicationResult> => {
  return executeRequest('/api/applications/import', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
}

export const mapImportErrorToMessage = (error: unknown) => {
  if (error instanceof ApiRequestError) {
    if (error.status === 409) {
      return `导入冲突：${error.message}`
    }

    if (error.status === 400) {
      return error.message
    }

    return `导入失败（HTTP ${error.status}）：${error.message}`
  }

  if (error instanceof Error) {
    return `导入失败：${error.message}`
  }

  return '导入失败：未知错误'
}
