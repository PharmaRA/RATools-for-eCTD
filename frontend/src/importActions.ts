import { ApiRequestError, apiFetch, buildJsonRequestInit } from './apiClient'
import type {
  ImportApplicationContract,
  ImportApplicationIssue,
  ImportApplicationResult,
} from './api/contracts'
import { buildApplicationsUrl } from './applicationActions'

export type ImportApplicationRequest = ImportApplicationContract
export type { ImportApplicationIssue, ImportApplicationResult } from './api/contracts'

const lifecycleTargetIssueCodes = new Set(['LIFECYCLE_TARGET_MISSING', 'LIFECYCLE_TARGET_NOT_IMPORTED'])

export const normalizeImportIssueSeverity = (severity: string) => String(severity).trim().toLowerCase()

export const isLifecycleTargetImportIssue = (issue: ImportApplicationIssue) => (
  lifecycleTargetIssueCodes.has(issue.code)
)

export const summarizeImportIssues = (issues: ImportApplicationIssue[]) => {
  const lifecycleIssues: ImportApplicationIssue[] = []
  const otherIssues: ImportApplicationIssue[] = []
  let warningCount = 0
  let errorCount = 0

  for (const issue of issues) {
    const severity = normalizeImportIssueSeverity(issue.severity)
    if (severity === 'warning') {
      warningCount += 1
    }

    if (severity === 'error') {
      errorCount += 1
    }

    if (isLifecycleTargetImportIssue(issue)) {
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

export const buildImportApplicationUrl = () => `${buildApplicationsUrl()}/import`

export const importApplication = async (
  request: ImportApplicationRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<ImportApplicationResult> => {
  return executeRequest(buildImportApplicationUrl(), buildJsonRequestInit('POST', request))
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
