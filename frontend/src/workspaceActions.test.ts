import { describe, expect, it, vi } from 'vitest'

import {
  deletePlacementWithDocument,
  movePlacementToSection,
  PlacementDeletePartialFailureError,
  revisePlacementMetadata,
  serializePlacementDragPayload,
  tryParsePlacementDragPayload,
} from './workspaceActions'
import { ApiRequestError } from './apiClient'

describe('workspaceActions', () => {
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
        fileNamePrefix: 'updated-report',
      },
      request,
    )

    expect(request).toHaveBeenCalledWith('/api/document-placements/placement-1/metadata', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: 'Updated title', fileNamePrefix: 'updated-report' }),
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
