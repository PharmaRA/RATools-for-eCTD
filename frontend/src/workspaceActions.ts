import { apiFetch, buildJsonRequestInit } from './apiClient'
import { buildApplicationUrl } from './applicationActions'
import type { EctdStructureResponse } from './pages/appShared'
import type { DocumentPlacementRecord, DocumentRecord } from './workspaceTree'

export const WORKSPACE_PLACEMENT_DRAG_MIME = 'application/x-ratools-placement'

export type PlacementDragPayload = {
  placementId: string
  documentId: string
  sectionPath: string
}

export type MovePlacementRequest = {
  placementId: string
  fromSection: string
  toSection: string
}

export type DeletePlacementWithDocumentRequest = {
  placementId: string
  documentId: string
}

export type RevisePlacementMetadataRequest = {
  placementId: string
  title?: string
  operation: string
  fileNamePrefix: string
  lifecycleTargetPlacementId?: string | null
}

export type UploadDocumentToSectionRequest = {
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
): Promise<DocumentPlacementRecord[] | { items?: DocumentPlacementRecord[] | null }> => {
  return executeRequest(buildWorkspaceDataUrls(appId).placements)
}

export const loadWorkspaceDocuments = async (
  appId: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<DocumentRecord[]> => {
  return executeRequest(buildWorkspaceDataUrls(appId).documents)
}

export const loadWorkspaceEctdStructure = async (
  appId: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<EctdStructureResponse> => {
  return executeRequest(buildWorkspaceDataUrls(appId).ectdStructure)
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
  if (request.fromSection.trim().toLowerCase() === request.toSection.trim().toLowerCase()) {
    return false
  }

  await executeRequest(
    buildDocumentPlacementSectionUrl(request.placementId),
    buildJsonRequestInit('PUT', { ctdSection: request.toSection }),
  )

  return true
}

export const deletePlacementWithDocument = async (
  request: DeletePlacementWithDocumentRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  await executeRequest(buildDocumentPlacementUrl(request.placementId), { method: 'DELETE' })

  try {
    await executeRequest(buildDocumentUrl(request.documentId), { method: 'DELETE' })
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

  const document = await executeRequest(
    buildDocumentUploadUrl(request.applicationId, request.sequenceNumber),
    { method: 'POST', body: formData },
  ) as { id: string }

  await executeRequest(
    buildDocumentPlacementsUrl(),
    buildJsonRequestInit('POST', {
      applicationId: request.applicationId,
      sequenceNumber: request.sequenceNumber,
      documentId: document.id,
      ctdSection: request.ctdSection,
      operation: 'New',
    }),
  )
}
