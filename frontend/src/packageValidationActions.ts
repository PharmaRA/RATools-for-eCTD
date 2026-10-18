import { apiFetch, apiFetchResponse, ApiRequestError, buildJsonRequestInit } from './apiClient'
import { downloadBlob, getDownloadFileName } from './browserDownload'
import type {
  CheckStatus, CreatePackageValidationRequest, PackageInputDto, PackageRunSummary,
  PackageRunSummaryPackageValidationPage, RegisterPackageInputRequest, ValidationFinding,
  ValidationFindingPackageValidationPage, ValidationProfileScope, ValidationSeverity,
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
export type ProjectionLeafAddress = { applicationId: string; sequenceNumber: string; backboneRelativePath: string; leafId: string }
export type ProjectionLeafStatus = 'Current' | 'Replaced' | 'Deleted' | 'DeleteEvent' | 'AppendMember' | 'Unknown'
export type ProjectionLeaf = {
  address: ProjectionLeafAddress; title: string | null; operation: string | null; contentLogicalPath: string | null
  status: ProjectionLeafStatus; replacedBy: string | null; deletedBy: string | null; appendedTo: string | null
  isRegionalReference: boolean; sourceSequence: string
}
export type ProjectionEvent = {
  source: ProjectionLeafAddress; target: ProjectionLeafAddress | null; operation: string
  status: CheckStatus; unresolvedReason: string | null
}
export type ProjectionBaselineEntry = {
  sequenceNumber: string; sourceKind: string; sourceId: string; contentDigest: string
  profileSnapshotId: string; trustStatus: string
}
export type ProjectionView = 'history' | 'current' | 'sequence'
// The archived document is returned as one explicit, paged view. It is derived
// evidence: an incomplete projection says so and never implies effectiveness.
export type ValidationProjectionPage = {
  schemaVersion: number; view: ProjectionView; sequenceNumber: string | null; applicationId: string
  targetSequence: string; profileSnapshotId: string; inputDigest: string; baselineDigest: string
  engineVersion: string; isComplete: boolean; incompleteReasons: string[]; appliedSequences: string[]
  baseline: ProjectionBaselineEntry[]; totalCount: number; offset: number; limit: number
  leaves: ProjectionLeaf[]; events: ProjectionEvent[]; findings: ValidationFinding[]
}
export type ExternalComparisonDifference = { externalRuleId: string; internalRuleId: string | null; externalMessage: string; explanation: string }
export type ExternalComparisonEvidence = {
  runId: string; toolName: string; toolVersion: string; packageDigest: string
  reportSha256: string; differences: ExternalComparisonDifference[]; recordedUtc: string; digest: string
}

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
export const loadExternalComparison = async (id: string, signal?: AbortSignal): Promise<ExternalComparisonEvidence | null> => {
  try {
    return await apiFetch(`${runUrl(id)}/external-report`, { signal })
  } catch (caught) {
    if (caught instanceof ApiRequestError && caught.status === 404) return null
    throw caught
  }
}
export const registerExternalComparison = (id: string, body: { toolName: string; toolVersion: string; packageDigest: string
  report: string; differences: ExternalComparisonDifference[] }): Promise<ExternalComparisonEvidence> =>
  apiFetch(`${runUrl(id)}/external-reports`, buildJsonRequestInit('POST', body))
export const loadExternalComparisonText = (id: string, signal?: AbortSignal): Promise<string> =>
  apiFetchResponse(`${runUrl(id)}/external-report/text`, { signal }).then(response => response.text())
export const loadValidationProjection = (id: string, view: ProjectionView, offset: number, sequenceNumber?: string,
  signal?: AbortSignal): Promise<ValidationProjectionPage> => {
  const query = new URLSearchParams({ view, offset: String(offset), limit: '50' })
  if (view === 'sequence' && sequenceNumber) query.set('sequenceNumber', sequenceNumber)
  return apiFetch(`${runUrl(id)}/projection?${query}`, { signal })
}
