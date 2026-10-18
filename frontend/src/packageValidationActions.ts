import { apiFetch, apiFetchResponse, buildJsonRequestInit } from './apiClient'
import { downloadBlob, getDownloadFileName } from './browserDownload'
import type {
  CheckStatus, CreatePackageValidationRequest, PackageInputDto, PackageRunSummary,
  PackageRunSummaryPackageValidationPage, RegisterPackageInputRequest, ValidationFindingPackageValidationPage,
  ValidationProfileScope, ValidationSeverity,
} from './api/generated'

export type FindingFilters = {
  severity?: ValidationSeverity; status?: CheckStatus; ruleId?: string
  category?: string; nodePath?: string; logicalPath?: string
}
export type ValidationEvidence = {
  inputId: string; comparisonInputId: string | null
  history: Array<{ inputId: string; profileSnapshotId: string }>
  ruleCatalog: { rules: Array<{ internalRuleId: string; title: string; category: string; sourceSection: string; implementationStatus: string }> }
  historyBaseline: { entries: Array<{ sequenceNumber: string; sourceId: string; trustStatus: string; contentDigest: string }> }
}
export type TextPreview = { logicalPath: string; sha256: string; startLine: number; lines: string[]; truncated: boolean }

const runs = '/api/package-validations'
const inputs = '/api/package-validation-inputs'
export const runUrl = (id: string) => `${runs}/${encodeURIComponent(id)}`
export const inputUrl = (id: string) => `${inputs}/${encodeURIComponent(id)}`
export const isValidationActive = (run?: PackageRunSummary) => run?.status === 'Pending' || run?.status === 'Running'
export const loadValidationProfiles = (signal?: AbortSignal): Promise<ValidationProfileScope[]> => apiFetch(`${runs}/profiles`, { signal })
export const loadValidationRun = (id: string, signal?: AbortSignal): Promise<PackageRunSummary> => apiFetch(runUrl(id), { signal })
export const loadValidationInput = (id: string, signal?: AbortSignal): Promise<PackageInputDto> => apiFetch(inputUrl(id), { signal })
export const loadValidationEvidence = (id: string, signal?: AbortSignal): Promise<ValidationEvidence> => apiFetch(`${runUrl(id)}/evidence`, { signal })
export const registerValidationInput = (body: RegisterPackageInputRequest, signal?: AbortSignal): Promise<PackageInputDto> =>
  apiFetch(inputs, { ...buildJsonRequestInit('POST', body), signal })
export const uploadValidationInput = (applicationId: string, file: File, sequenceNumber?: string, rootRelativePath?: string,
  signal?: AbortSignal): Promise<PackageInputDto> => {
  const body = new FormData()
  body.append('applicationId', applicationId)
  body.append('file', file)
  if (sequenceNumber) body.append('sequenceNumber', sequenceNumber)
  if (rootRelativePath !== undefined) body.append('rootRelativePath', rootRelativePath)
  return apiFetch(`${inputs}/upload`, { method: 'POST', body, signal })
}
export const createValidationRun = (body: CreatePackageValidationRequest): Promise<PackageRunSummary> =>
  apiFetch(runs, buildJsonRequestInit('POST', body))
export const cancelValidationRun = (id: string): Promise<PackageRunSummary> => apiFetch(`${runUrl(id)}/cancel`, { method: 'POST' })
export const loadValidationRuns = (offset: number, applicationId?: string, signal?: AbortSignal): Promise<PackageRunSummaryPackageValidationPage> => {
  const query = new URLSearchParams({ offset: String(offset), limit: '20' })
  if (applicationId) query.set('applicationId', applicationId)
  return apiFetch(`${runs}?${query}`, { signal })
}
export const findingsUrl = (id: string, offset: number, filters: FindingFilters) => {
  const query = new URLSearchParams({ offset: String(offset), limit: '20' })
  for (const [name, value] of Object.entries(filters)) if (value) query.set(name, value)
  return `${runUrl(id)}/findings?${query}`
}
export const loadValidationFindings = (id: string, offset: number, filters: FindingFilters, signal?: AbortSignal): Promise<ValidationFindingPackageValidationPage> =>
  apiFetch(findingsUrl(id, offset, filters), { signal })
export const loadInputText = (id: string, logicalPath: string, line = 1, signal?: AbortSignal, surrounding = false): Promise<TextPreview> =>
  apiFetch(`${inputUrl(id)}/text?${new URLSearchParams({ logicalPath, line: String(line), surrounding: String(surrounding) })}`, { signal })
export const downloadValidationFile = async (id: string, logicalPath: string, surrounding = false) => {
  const response = await apiFetchResponse(`${inputUrl(id)}/file?${new URLSearchParams({ logicalPath, surrounding: String(surrounding) })}`)
  downloadBlob(await response.blob(), getDownloadFileName(response.headers.get('content-disposition'), logicalPath.split('/').pop() || 'package-file'))
}
export const downloadValidationJson = async (id: string, kind: 'report' | 'evidence' | 'manifest') => {
  const response = await apiFetchResponse(`${kind === 'manifest' ? inputUrl(id) : runUrl(id)}/${kind}`)
  downloadBlob(await response.blob(), `validation-${id}-${kind}.json`)
}
