import { ApiRequestError, apiFetch, buildJsonRequestInit } from './apiClient'

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

const lifecycleTargetIssueCodes = new Set(['LIFECYCLE_TARGET_MISSING', 'LIFECYCLE_TARGET_NOT_IMPORTED'])

export const summarizeImportIssues = (issues: ImportApplicationIssue[]) => {
  const lifecycleIssues: ImportApplicationIssue[] = []
  const otherIssues: ImportApplicationIssue[] = []
  let warningCount = 0
  let errorCount = 0

  for (const issue of issues) {
    const severity = String(issue.severity).trim().toLowerCase()
    if (severity === 'warning') {
      warningCount += 1
    }

    if (severity === 'error') {
      errorCount += 1
    }

    if (lifecycleTargetIssueCodes.has(issue.code)) {
      lifecycleIssues.push(issue)
    } else {
      otherIssues.push(issue)
    }
  }

  return {
    lifecycleIssues,
    otherIssues,
    warningCount,
    errorCount,
  }
}

export const importApplication = async (
  request: ImportApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ImportApplicationResult> => {
  return executeRequest('/api/applications/import', buildJsonRequestInit('POST', request))
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
