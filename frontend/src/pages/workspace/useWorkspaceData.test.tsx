import { act, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  buildWorkspaceExpandedKeys,
  buildWorkspaceDocumentsById,
  getWorkspaceEctdRootsFromResponse,
  getWorkspacePlacementsFromResponse,
  splitWorkspacePlacements,
  useWorkspaceData,
} from './useWorkspaceData'
import type { apiFetch as defaultApiFetch } from '../../apiClient'

type UseWorkspaceDataOptions = {
  appId: string
  seqNumber: string
  apiFetch: typeof defaultApiFetch
}

type UseWorkspaceDataResult = ReturnType<typeof useWorkspaceData>

const waitForExpectation = async (assertion: () => void) => {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      assertion()
      return
    } catch (error) {
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 0))
      })

      if (attempt === 19) {
        throw error
      }
    }
  }
}

const renderUseWorkspaceData = (options: UseWorkspaceDataOptions) => {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  let current: UseWorkspaceDataResult | null = null

  const Probe = () => {
    const value = useWorkspaceData(options)
    useEffect(() => {
      current = value
    })
    return null
  }

  act(() => {
    root.render(<Probe />)
  })

  return {
    get current() {
      if (!current) {
        throw new Error('Hook did not render.')
      }

      return current
    },
    unmount: () => {
      act(() => {
        root.unmount()
      })
      host.remove()
    },
  }
}

describe('useWorkspaceData', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('reads workspace placements from list or paged response data', () => {
    const placements = [
      { id: 'placement-1', applicationId: 'app-1', sequenceNumber: '0000', documentId: 'doc-1', ctdSection: '1.2', operation: 'New' },
    ]

    expect(getWorkspacePlacementsFromResponse(placements)).toBe(placements)
    expect(getWorkspacePlacementsFromResponse({ items: placements })).toBe(placements)
    expect(getWorkspacePlacementsFromResponse({})).toEqual([])
    expect(getWorkspacePlacementsFromResponse(null)).toEqual([])
    expect(getWorkspacePlacementsFromResponse(undefined)).toEqual([])
  })

  it('builds workspace documents by id from optional document data', () => {
    const docs = [
      { id: 'doc-1', fileName: 'cover.pdf', storagePath: '/tmp/cover.pdf' },
      { id: 'doc-2', fileName: 'summary.pdf', storagePath: '/tmp/summary.pdf' },
    ]

    expect(buildWorkspaceDocumentsById(docs)).toEqual({
      'doc-1': docs[0],
      'doc-2': docs[1],
    })
    expect(buildWorkspaceDocumentsById(null)).toEqual({})
    expect(buildWorkspaceDocumentsById(undefined)).toEqual({})
  })

  it('reads workspace eCTD roots from optional structure data', () => {
    const roots = [
      { elementName: 'm1', sectionPath: '1.2', displayName: 'Cover', sourceProfile: 'FDA', children: [] },
    ]

    expect(getWorkspaceEctdRootsFromResponse({ roots })).toBe(roots)
    expect(getWorkspaceEctdRootsFromResponse({})).toEqual([])
    expect(getWorkspaceEctdRootsFromResponse(null)).toEqual([])
    expect(getWorkspaceEctdRootsFromResponse(undefined)).toEqual([])
  })

  it('builds expanded keys from workspace eCTD roots', () => {
    expect(buildWorkspaceExpandedKeys([
      { elementName: 'm1', sectionPath: '1.2', displayName: 'Cover', sourceProfile: 'FDA', children: [] },
      { elementName: 'm2', sectionPath: '2.3', displayName: 'Quality', sourceProfile: 'FDA', children: [] },
    ])).toEqual(['1.2', '2.3'])

    expect(buildWorkspaceExpandedKeys([])).toEqual([])
  })

  it('splits application and sequence placements together', () => {
    const currentSequencePlacement = { id: 'placement-1', applicationId: 'app-1', sequenceNumber: '0000', documentId: 'doc-1', ctdSection: '1.2', operation: 'New' }
    const otherSequencePlacement = { id: 'placement-2', applicationId: 'app-1', sequenceNumber: '0001', documentId: 'doc-2', ctdSection: '1.3', operation: 'Replace' }
    const otherApplicationPlacement = { id: 'placement-3', applicationId: 'app-2', sequenceNumber: '0000', documentId: 'doc-3', ctdSection: '1.4', operation: 'New' }

    const result = splitWorkspacePlacements(
      [currentSequencePlacement, otherSequencePlacement, otherApplicationPlacement],
      'app-1',
      '0000',
    )

    expect(result.applicationPlacements).toEqual([currentSequencePlacement, otherSequencePlacement])
    expect(result.sequencePlacements).toEqual([currentSequencePlacement])
  })

  it('loads placements, documents, structure, and derived tree data', async () => {
    const apiFetch = vi.fn()
      .mockImplementation((url: string) => {
        if (url === '/api/document-placements?applicationId=app-1') {
          return Promise.resolve([
            { id: 'placement-1', applicationId: 'app-1', sequenceNumber: '0000', documentId: 'doc-1', ctdSection: '1.2', operation: 'New' },
          ])
        }

        if (url === '/api/documents?applicationId=app-1') {
          return Promise.resolve([
            { id: 'doc-1', fileName: 'cover.pdf', storagePath: '/tmp/cover.pdf' },
          ])
        }

        if (url === '/api/applications/app-1/ectd-structure') {
          return Promise.resolve({
            roots: [{ elementName: 'm1', sectionPath: '1.2', displayName: 'Cover', sourceProfile: 'FDA', children: [] }],
          })
        }

        return Promise.reject(new Error(`Unexpected URL ${url}`))
      })

    const result = renderUseWorkspaceData({ appId: 'app-1', seqNumber: '0000', apiFetch })

    await waitForExpectation(() => expect(result.current.treeData).toHaveLength(1))

    expect(result.current.placements).toHaveLength(1)
    expect(result.current.documentsById['doc-1'].fileName).toBe('cover.pdf')
    expect(result.current.treeData[0].children).toHaveLength(1)
    expect(result.current.treeError).toBeNull()
    expect(result.current.placementsError).toBeNull()
    expect(result.current.documentsError).toBeNull()
    result.unmount()
  })

  it('stores visible placement and document load errors', async () => {
    const apiFetch = vi.fn()
      .mockImplementation((url: string) => {
        if (url === '/api/document-placements?applicationId=app-1') {
          return Promise.reject(new Error('placements unavailable'))
        }

        if (url === '/api/documents?applicationId=app-1') {
          return Promise.reject(new Error('documents unavailable'))
        }

        if (url === '/api/applications/app-1/ectd-structure') {
          return Promise.resolve({ roots: [] })
        }

        return Promise.reject(new Error(`Unexpected URL ${url}`))
      })

    const result = renderUseWorkspaceData({ appId: 'app-1', seqNumber: '0000', apiFetch })

    await waitForExpectation(() => expect(result.current.placementsError).toBe('placements unavailable'))
    expect(result.current.documentsError).toBe('documents unavailable')
    result.unmount()
  })
})
