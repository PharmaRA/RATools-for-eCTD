import { apiFetch, apiFetchResponse, buildJsonRequestInit } from './apiClient'
import { downloadBlob, getDownloadFileName } from './browserDownload'
import type {
  DeliveryReceipt, DeliveryRecord, FinalizationDraftResult, FinalizationResult, FinalizedSequence,
  FinalizeSequenceBody, OpenDraftBody, PackageRunSummary, RegisterDeliveryBody, RegisterReceiptBody,
  ReleaseManifest, StandardsSnapshotView, ValidationReport,
} from './api/generated'
import { loadValidationRun, runUrl } from './packageValidationActions'

const canonical = (value: unknown): string => {
  if (value === null || typeof value !== 'object') return JSON.stringify(value)
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`
  return `{${Object.entries(value).filter(([, item]) => item !== undefined).sort(([a], [b]) => a.localeCompare(b))
    .map(([key, item]) => `${JSON.stringify(key)}:${canonical(item)}`).join(',')}}`
}

export type FinalizationEvidence = { run: PackageRunSummary; report: ValidationReport; standards: StandardsSnapshotView }
export const buildFinalizationBody = (appId: string, seqNumber: string, revision: number | undefined,
  run: PackageRunSummary, report: ValidationReport, standards: StandardsSnapshotView, key: string): FinalizeSequenceBody => {
  if (revision === undefined || !Number.isSafeInteger(revision) || revision < 0) throw new Error('工作区修订不可用，请刷新后重新确认。')
  if (run.applicationId !== appId || run.sequenceNumber !== seqNumber) throw new Error('该验证运行属于其他申请或序列。')
  if (run.status !== 'Completed') throw new Error('请选择已完成的验证运行。')
  if (!run.reportAvailable || report.runStatus !== 'Completed' || !report.executionCompleted) throw new Error('验证报告尚未完整归档。')
  if (canonical(run.binding) !== canonical(report.binding)) throw new Error('报告与验证运行的证据绑定不一致。')
  if (!run.binding.standardsSnapshotId || standards.id !== run.binding.standardsSnapshotId ||
    standards.ruleProfileAlias !== run.binding.profileSnapshotId) throw new Error('缺少匹配的精确标准快照，请重新验证。')
  if (run.binding.validationMode === 'Formal' && !report.isReadyForFinalization) {
    throw new Error(`未满足正式定稿要求：${report.blockingReasons.join('；')}`)
  }
  return { expectedRevision: revision, packageValidationRunId: run.id,
    standardsProfileSnapshotId: run.binding.profileSnapshotId, ichEctdVersion: standards.ichEctdVersion,
    regionalModule1Version: standards.regionalModule1Version, historyBaselineDigest: run.binding.historyBaselineDigest,
    idempotencyKey: key }
}

export const buildDeliveryBody = (snapshot: FinalizedSequence, manifest: ReleaseManifest,
  fields: Omit<RegisterDeliveryBody, 'packageSha256'>): RegisterDeliveryBody => {
  if (snapshot.validationMode !== 'Formal' || !snapshot.isReadyForFinalization) throw new Error('仅就绪的正式快照可登记交付。')
  if (manifest.digest !== snapshot.manifestDigest || manifest.applicationId !== snapshot.applicationId ||
    manifest.sequenceNumber !== snapshot.sequenceNumber || manifest.sourceWorkspaceRevision !== snapshot.sourceWorkspaceRevision) {
    throw new Error('清单与所选快照不一致。')
  }
  const packages = manifest.files.filter(file => file.relativePath === 'delivery-package.zip' && file.kind === 3)
  if (packages.length !== 1 || !/^[a-f0-9]{64}$/.test(packages[0].sha256) || packages[0].length <= 0) {
    throw new Error('缺少唯一有效的交付 ZIP 证据。')
  }
  return { ...fields, packageSha256: packages[0].sha256 }
}

const sequenceUrl = (appId: string, seq: string) => `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seq)}`
const snapshotUrl = (id: string) => `/api/finalizations/${encodeURIComponent(id)}`
const deliveriesUrl = (id: string) => `/api/finalized-sequences/${encodeURIComponent(id)}/deliveries`
const receiptsUrl = (id: string) => `/api/finalized-deliveries/${encodeURIComponent(id)}/receipts`
export const loadFinalizations = (appId: string, seq: string, signal?: AbortSignal): Promise<FinalizedSequence[]> =>
  apiFetch(`${sequenceUrl(appId, seq)}/finalized-versions`, { signal })
export const loadReleaseManifest = (id: string, signal?: AbortSignal): Promise<ReleaseManifest> => apiFetch(`${snapshotUrl(id)}/manifest`, { signal })
export const loadFinalizationEvidence = async (runId: string, signal?: AbortSignal): Promise<FinalizationEvidence> => {
  const run = await loadValidationRun(runId, signal)
  if (!run.reportAvailable || run.status !== 'Completed') throw new Error('该运行尚无完整报告。')
  if (!run.binding.standardsSnapshotId) throw new Error('旧运行缺少精确标准快照，请重新验证。')
  const [report, standards] = await Promise.all([
    apiFetch(`${runUrl(runId)}/report`, { signal }) as Promise<ValidationReport>,
    apiFetch(`/api/standards-snapshots/${encodeURIComponent(run.binding.standardsSnapshotId)}`, { signal }) as Promise<StandardsSnapshotView>,
  ])
  return { run, report, standards }
}
export const finalizeSequence = (appId: string, seq: string, body: FinalizeSequenceBody): Promise<FinalizationResult> =>
  apiFetch(`${sequenceUrl(appId, seq)}/finalizations`, buildJsonRequestInit('POST', body))
export const openFinalizedDraft = (id: string, body: OpenDraftBody): Promise<FinalizationDraftResult> =>
  apiFetch(`/api/finalized-sequences/${encodeURIComponent(id)}/draft-revisions`, buildJsonRequestInit('POST', body))
export const loadDeliveries = (id: string, signal?: AbortSignal): Promise<DeliveryRecord[]> => apiFetch(deliveriesUrl(id), { signal })
export const registerDelivery = (id: string, body: RegisterDeliveryBody): Promise<DeliveryRecord> => apiFetch(deliveriesUrl(id), buildJsonRequestInit('POST', body))
export const loadDeliveryReceipts = (id: string, signal?: AbortSignal): Promise<DeliveryReceipt[]> => apiFetch(receiptsUrl(id), { signal })
export const registerDeliveryReceipt = (id: string, body: RegisterReceiptBody): Promise<DeliveryReceipt> => apiFetch(receiptsUrl(id), buildJsonRequestInit('POST', body))
export const downloadRelease = async (id: string, kind: 'manifest' | 'validation-report.json' | 'delivery-package.zip') => {
  const response = await apiFetchResponse(kind === 'manifest' ? `${snapshotUrl(id)}/manifest`
    : `${snapshotUrl(id)}/files?${new URLSearchParams({ path: kind })}`)
  downloadBlob(await response.blob(), getDownloadFileName(response.headers.get('content-disposition'),
    kind === 'manifest' ? `manifest-${id}.json` : kind))
}
