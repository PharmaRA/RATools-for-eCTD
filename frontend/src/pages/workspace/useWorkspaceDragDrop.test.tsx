import { act, useEffect, type KeyboardEvent } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useWorkspaceDragDrop } from './useWorkspaceDragDrop'

type DragDropResult = ReturnType<typeof useWorkspaceDragDrop>

const renderUseWorkspaceDragDrop = (options: Parameters<typeof useWorkspaceDragDrop>[0]) => {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  let current: DragDropResult | null = null

  const Probe = () => {
    const value = useWorkspaceDragDrop(options)
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

describe('useWorkspaceDragDrop', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('uploads all valid dropped files and reports invalid extensions', async () => {
    const uploadFile = vi.fn().mockResolvedValue(undefined)
    const messageApi = {
      error: vi.fn(),
      warning: vi.fn(),
    }
    const files = [
      new File(['one'], 'one.pdf', { type: 'application/pdf' }),
      new File(['two'], 'two.xml', { type: 'text/xml' }),
      new File(['bad'], 'bad.exe', { type: 'application/octet-stream' }),
    ]
    const hook = renderUseWorkspaceDragDrop({
      placements: [],
      movePlacement: vi.fn(),
      uploadFile,
      messageApi,
    })

    await act(async () => {
      await hook.current.dropFiles(files, '1.2')
    })

    expect(uploadFile).toHaveBeenCalledTimes(2)
    expect(uploadFile).toHaveBeenNthCalledWith(1, files[0], '1.2')
    expect(uploadFile).toHaveBeenNthCalledWith(2, files[1], '1.2')
    expect(messageApi.error).toHaveBeenCalledWith(expect.stringContaining('Unsupported file extension'))
    expect(messageApi.error).toHaveBeenCalledWith(expect.stringContaining('bad.exe'))
    hook.unmount()
  })

  it('moves a keyboard-selected document to the next selected section', async () => {
    const movePlacement = vi.fn().mockResolvedValue(undefined)
    const hook = renderUseWorkspaceDragDrop({
      placements: [{ id: 'placement-1', applicationId: 'app-1', sequenceNumber: '0000', documentId: 'doc-1', ctdSection: '1.2', operation: 'New' }],
      movePlacement,
      uploadFile: vi.fn(),
    })
    const preventDefault = vi.fn()
    const stopPropagation = vi.fn()
    const enterEvent = {
      key: 'Enter',
      preventDefault,
      stopPropagation,
    } as unknown as KeyboardEvent<HTMLElement>

    await act(async () => {
      await hook.current.handleNodeKeyDown(enterEvent, {
        nodeType: 'document',
        key: 'placement:placement-1',
        placementId: 'placement-1',
        documentId: 'doc-1',
        sectionPath: '1.2',
        title: 'cover.pdf',
        operation: 'New',
        children: [],
      }, false)
    })

    await act(async () => {
      await hook.current.handleNodeKeyDown(enterEvent, {
        nodeType: 'section',
        key: '1.3',
        sectionPath: '1.3',
        title: '1.3 Administrative',
        canDrop: true,
        hasPlacement: false,
        children: [],
      }, true)
    })

    expect(movePlacement).toHaveBeenCalledWith('placement-1', '1.2', '1.3')
    expect(preventDefault).toHaveBeenCalled()
    expect(stopPropagation).toHaveBeenCalled()
    hook.unmount()
  })
})
