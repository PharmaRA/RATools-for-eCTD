import { apiFetch } from './apiClient'

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

  await executeRequest(`/api/document-placements/${request.placementId}/section`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ctdSection: request.toSection }),
  })

  return true
}

export const deletePlacementWithDocument = async (
  request: DeletePlacementWithDocumentRequest,
  executeRequest: typeof apiFetch = apiFetch,
) => {
  await executeRequest(`/api/document-placements/${request.placementId}`, { method: 'DELETE' })

  try {
    await executeRequest(`/api/documents/${request.documentId}`, { method: 'DELETE' })
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
  await executeRequest(`/api/document-placements/${request.placementId}/metadata`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: request.title,
      operation: request.operation,
      fileNamePrefix: request.fileNamePrefix,
      lifecycleTargetPlacementId: request.lifecycleTargetPlacementId,
    }),
  })
}
