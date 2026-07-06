import { describe, expect, it, vi } from 'vitest'

import {
  buildDocumentPlacementMetadataUrl,
  buildDocumentPlacementSectionUrl,
  buildDocumentPlacementUrl,
  buildDocumentUrl,
  buildDocumentUploadUrl,
  buildWorkspaceDataUrls,
  deletePlacementWithDocument,
  loadWorkspaceDocuments,
  loadWorkspaceEctdStructure,
  loadWorkspacePlacements,
  movePlacementToSection,
  PlacementDeletePartialFailureError,
  revisePlacementMetadata,
  serializePlacementDragPayload,
  tryParsePlacementDragPayload,
  uploadDocumentToSection,
} from './workspaceActions'
import { ApiRequestError } from './apiClient'

describe('workspaceActions', () => {
  it('builds workspace data URLs with encoded application ids', () => {
    expect(buildWorkspaceDataUrls('app 1/2')).toEqual({
      placements: '/api/document-placements?applicationId=app%201%2F2',
      documents: '/api/documents?applicationId=app%201%2F2',
      ectdStructure: '/api/applications/app%201%2F2/ectd-structure',
    })
  })

  it('loads workspace placements for an application', async () => {
    const placements = [{ id: 'placement-1' }]
    const request = vi.fn().mockResolvedValue(placements)

    const result = await loadWorkspacePlacements('app-1', request)

    expect(request).toHaveBeenCalledWith('/api/document-placements?applicationId=app-1')
    expect(result).toEqual(placements)
  })

  it('loads workspace documents for an application', async () => {
    const documents = [{ id: 'document-1' }]
    const request = vi.fn().mockResolvedValue(documents)

    const result = await loadWorkspaceDocuments('app-1', request)

    expect(request).toHaveBeenCalledWith('/api/documents?applicationId=app-1')
    expect(result).toEqual(documents)
  })

  it('loads workspace eCTD structure for an application', async () => {
    const structure = { roots: [] }
    const request = vi.fn().mockResolvedValue(structure)

    const result = await loadWorkspaceEctdStructure('app-1', request)

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/ectd-structure')
    expect(result).toEqual(structure)
  })

  it('builds document placement mutation URLs', () => {
    expect(buildDocumentPlacementUrl('placement-1')).toBe('/api/document-placements/placement-1')
    expect(buildDocumentPlacementSectionUrl('placement-1')).toBe('/api/document-placements/placement-1/section')
    expect(buildDocumentPlacementMetadataUrl('placement-1')).toBe('/api/document-placements/placement-1/metadata')
  })

  it('builds workspace document mutation URLs', () => {
    expect(buildDocumentUrl('document-1')).toBe('/api/documents/document-1')
    expect(buildDocumentUploadUrl('app-1', '0001')).toBe('/api/applications/app-1/sequences/0001/documents/upload')
  })

  it('calls placement section update endpoint for move', async () => {
    const request = vi.fn().mockResolvedValue({})

    await movePlacementToSection(
      {
        placementId: 'placement-1',
        fromSection: 'm1.1',
        toSection: 'm1.2',
      },
      request,
    )

    expect(request).toHaveBeenCalledWith('/api/document-placements/placement-1/section', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ctdSection: 'm1.2' }),
    })
  })

  it('skips move request when target section is unchanged', async () => {
    const request = vi.fn()

    const moved = await movePlacementToSection(
      {
        placementId: 'placement-1',
        fromSection: 'm1.1',
        toSection: 'm1.1',
      },
      request,
    )

    expect(moved).toBe(false)
    expect(request).not.toHaveBeenCalled()
  })

  it('deletes placement first then deletes backing document', async () => {
    const request = vi.fn().mockResolvedValue({})

    await deletePlacementWithDocument(
      {
        placementId: 'placement-1',
        documentId: 'document-1',
      },
      request,
    )

    expect(request).toHaveBeenNthCalledWith(1, '/api/document-placements/placement-1', { method: 'DELETE' })
    expect(request).toHaveBeenNthCalledWith(2, '/api/documents/document-1', { method: 'DELETE' })
  })

  it('updates placement title and file name prefix for revision', async () => {
    const request = vi.fn().mockResolvedValue({})

    await revisePlacementMetadata(
      {
        placementId: 'placement-1',
        title: 'Updated title',
        operation: 'Replace',
        fileNamePrefix: 'updated-report',
        lifecycleTargetPlacementId: 'target-placement-1',
      },
      request,
    )

    expect(request).toHaveBeenCalledWith('/api/document-placements/placement-1/metadata', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: 'Updated title', operation: 'Replace', fileNamePrefix: 'updated-report', lifecycleTargetPlacementId: 'target-placement-1' }),
    })
  })

  it('uploads a document file then maps it to the target section', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({ id: 'document-1' })
      .mockResolvedValueOnce({})
    const file = new File(['content'], 'leaf.pdf', { type: 'application/pdf' })

    await uploadDocumentToSection({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      file,
      ctdSection: 'm1.2',
    }, request)

    expect(request).toHaveBeenCalledTimes(2)
    expect(request.mock.calls[0][0]).toBe('/api/applications/app-1/sequences/0001/documents/upload')
    expect(request.mock.calls[0][1].method).toBe('POST')
    const uploadBody = request.mock.calls[0][1].body as FormData
    expect(uploadBody.get('file')).toBe(file)
    expect(uploadBody.get('CtdSection')).toBe('m1.2')
    expect(request).toHaveBeenNthCalledWith(2, '/api/document-placements', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationId: 'app-1',
        sequenceNumber: '0001',
        documentId: 'document-1',
        ctdSection: 'm1.2',
        operation: 'New',
      }),
    })
  })

  it('throws partial failure error when placement delete succeeds but document delete fails', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({})
      .mockRejectedValueOnce(new ApiRequestError(409, 'Document cannot be deleted because references exist.'))

    await expect(deletePlacementWithDocument(
      {
        placementId: 'placement-1',
        documentId: 'document-1',
      },
      request,
    )).rejects.toBeInstanceOf(PlacementDeletePartialFailureError)
  })

  it('serializes and parses placement drag payload', () => {
    const encoded = serializePlacementDragPayload({
      placementId: 'placement-1',
      documentId: 'document-1',
      sectionPath: 'm1.1',
    })

    expect(tryParsePlacementDragPayload(encoded)).toEqual({
      placementId: 'placement-1',
      documentId: 'document-1',
      sectionPath: 'm1.1',
    })
  })

  it('returns null for invalid drag payload', () => {
    expect(tryParsePlacementDragPayload('not-json')).toBeNull()
    expect(tryParsePlacementDragPayload(JSON.stringify({ placementId: '', documentId: 'a', sectionPath: 'm1.1' }))).toBeNull()
  })
})
