import { apiFetch, buildJsonRequestInit } from './apiClient'
import type {
  DocumentContract,
  DocumentPlacementContract,
  EctdStructureContract,
} from './api/contracts'
import { buildApplicationUrl } from './applicationActions'
import type { WorkspaceSnapshotDto } from './api/generated'

export const WORKSPACE_PLACEMENT_DRAG_MIME = 'application/x-ratools-placement'

export type PlacementDragPayload = {
  placementId: string
  documentId: string
  sectionPath: string
}

export type MovePlacementRequest = {
  nodeInstanceId?: string
  sortOrder?: number
  expectedRevision: number
  placementId: string
  fromSection: string
  toSection: string
}

export type DeletePlacementWithDocumentRequest = {
  expectedRevision: number
  placementId: string
  documentId: string
}

export type RevisePlacementMetadataRequest = {
  expectedRevision: number
  placementId: string
  title?: string
  operation: string
  fileNamePrefix: string
  lifecycleTargetPlacementId?: string | null
}

export type UploadDocumentToSectionRequest = {
  nodeInstanceId?: string
  sortOrder?: number
  expectedRevision: number
  applicationId: string
  sequenceNumber: string
  file: File
  ctdSection: string
}

const buildApplicationScopedCollectionUrl = (baseUrl: string, applicationId?: string) => {
  if (applicationId === undefined) {
    return baseUrl
  }

  return `${baseUrl}?applicationId=${encodeURIComponent(applicationId)}`
}

export const buildDocumentPlacementsUrl = (applicationId?: string) => {
  return buildApplicationScopedCollectionUrl('/api/document-placements', applicationId)
}

export const buildDocumentsUrl = (applicationId?: string) => {
  return buildApplicationScopedCollectionUrl('/api/documents', applicationId)
}

export const buildWorkspaceEctdStructureUrl = (applicationId: string) => {
  return `${buildApplicationUrl(encodeURIComponent(applicationId))}/ectd-structure`
}

export const buildWorkspaceDataUrls = (appId: string) => {
  return {
    placements: buildDocumentPlacementsUrl(appId),
    documents: buildDocumentsUrl(appId),
    ectdStructure: buildWorkspaceEctdStructureUrl(appId),
  }
}

export const loadWorkspacePlacements = async (
  appId: string,
  executeRequest: typeof apiFetch = apiFetch,
  signal?: AbortSignal,
): Promise<DocumentPlacementContract[]> => {
  const url = buildWorkspaceDataUrls(appId).placements
  return signal ? executeRequest(url, { signal }) : executeRequest(url)
}

export const loadWorkspaceSnapshot = async (
  applicationId: string,
  sequenceNumber: string,
  executeRequest: typeof apiFetch = apiFetch,
  signal?: AbortSignal,
): Promise<WorkspaceSnapshotDto> => executeRequest(
  `/api/applications/${encodeURIComponent(applicationId)}/sequences/${encodeURIComponent(sequenceNumber)}/workspace`,
  { signal },
)

export const requireWorkspaceRevision = (revision: number | undefined | null): number => {
  if (revision === undefined || revision === null || !Number.isSafeInteger(revision) || revision < 0 || revision >= Number.MAX_SAFE_INTEGER) {
    throw new Error('请刷新工作区，加载当前修订号后再编辑。')
  }
  return revision
}

export const loadWorkspaceDocuments = async (
  appId: string,
  executeRequest: typeof apiFetch = apiFetch,
  signal?: AbortSignal,
): Promise<DocumentContract[]> => {
  const url = buildWorkspaceDataUrls(appId).documents
  return signal ? executeRequest(url, { signal }) : executeRequest(url)
}

export const loadWorkspaceEctdStructure = async (
  appId: string,
  executeRequest: typeof apiFetch = apiFetch,
  signal?: AbortSignal,
): Promise<EctdStructureContract> => {
  const url = buildWorkspaceDataUrls(appId).ectdStructure
  return signal ? executeRequest(url, { signal }) : executeRequest(url)
}

export const buildDocumentPlacementUrl = (placementId: string) => {
  return `${buildDocumentPlacementsUrl()}/${placementId}`
}

export const buildDocumentPlacementSectionUrl = (placementId: string) => {
  return `${buildDocumentPlacementUrl(placementId)}/section`
}

export const buildDocumentPlacementMetadataUrl = (placementId: string) => {
  return `${buildDocumentPlacementUrl(placementId)}/metadata`
}

export const buildDocumentUrl = (documentId: string) => `${buildDocumentsUrl()}/${documentId}`

export const buildDocumentUploadUrl = (applicationId: string, sequenceNumber: string) => {
  return `/api/applications/${applicationId}/sequences/${sequenceNumber}/documents/upload`
}

export class PlacementDeletePartialFailureError extends Error {
  readonly causeError: unknown

  constructor(message: string, causeError: unknown) {
    super(message)
    this.name = 'PlacementDeletePartialFailureError'
    this.causeError = causeError
  }
}

export const serializePlacementDragPayload = (payload: PlacementDragPayload) => JSON.stringify(payload)

export const tryParsePlacementDragPayload = (raw: string | null | undefined): PlacementDragPayload | null => {
  if (!raw) {
    return null
  }

  try {
    const parsed = JSON.parse(raw) as Partial<PlacementDragPayload>
    if (!parsed || typeof parsed !== 'object') {
      return null
    }

    if (
      typeof parsed.placementId !== 'string' || parsed.placementId.trim().length === 0
      || typeof parsed.documentId !== 'string' || parsed.documentId.trim().length === 0
      || typeof parsed.sectionPath !== 'string' || parsed.sectionPath.trim().length === 0
    ) {
      return null
    }

    return {
      placementId: parsed.placementId.trim(),
      documentId: parsed.documentId.trim(),
      sectionPath: parsed.sectionPath.trim(),
    }
  } catch {
    return null
  }
}

export const movePlacementToSection = async (
  request: MovePlacementRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<boolean> => {
  if (!request.nodeInstanceId && request.fromSection.trim().toLowerCase() === request.toSection.trim().toLowerCase()) {
    return false
  }

  await executeRequest(
    buildDocumentPlacementSectionUrl(request.placementId),
    buildJsonRequestInit('PUT', { ctdSection: request.toSection, nodeInstanceId: request.nodeInstanceId, sortOrder: request.sortOrder,
      expectedRevision: requireWorkspaceRevision(request.expectedRevision) }),
  )

  return true
}

export const deletePlacementWithDocument = async (
  request: DeletePlacementWithDocumentRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  const revision = requireWorkspaceRevision(request.expectedRevision)
  await executeRequest(`${buildDocumentPlacementUrl(request.placementId)}?expectedRevision=${revision}`, { method: 'DELETE' })

  try {
    await executeRequest(`${buildDocumentUrl(request.documentId)}?expectedRevision=${revision + 1}`, { method: 'DELETE' })
  } catch (error) {
    throw new PlacementDeletePartialFailureError(
      `Placement ${request.placementId} was deleted, but failed to delete document ${request.documentId}.`,
      error,
    )
  }
}

export const revisePlacementMetadata = async (
  request: RevisePlacementMetadataRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  await executeRequest(
    buildDocumentPlacementMetadataUrl(request.placementId),
    buildJsonRequestInit('PUT', {
      expectedRevision: requireWorkspaceRevision(request.expectedRevision),
      title: request.title,
      operation: request.operation,
      fileNamePrefix: request.fileNamePrefix,
      lifecycleTargetPlacementId: request.lifecycleTargetPlacementId,
    }),
  )
}

export const uploadDocumentToSection = async (
  request: UploadDocumentToSectionRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  const formData = new FormData()
  formData.append('file', request.file)
  formData.append('CtdSection', request.ctdSection)
  if (request.nodeInstanceId) formData.append('NodeInstanceId', request.nodeInstanceId)
  formData.append('ExpectedRevision', String(requireWorkspaceRevision(request.expectedRevision)))

  const document = await executeRequest(
    buildDocumentUploadUrl(request.applicationId, request.sequenceNumber),
    { method: 'POST', body: formData },
  ) as DocumentContract

  return await executeRequest(
    buildDocumentPlacementsUrl(),
    buildJsonRequestInit('POST', {
      expectedRevision: requireWorkspaceRevision(document.workspaceRevision),
      applicationId: request.applicationId,
      sequenceNumber: request.sequenceNumber,
      documentId: document.id,
      ctdSection: request.ctdSection,
      nodeInstanceId: request.nodeInstanceId,
      sortOrder: request.sortOrder ?? 0,
      operation: 'New',
    }),
  ) as DocumentPlacementContract
}
