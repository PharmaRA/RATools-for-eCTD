import { act } from 'react-dom/test-utils'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { type DirectoryBrowseResult, type DirectoryResolutionResult, type filesystemActions } from './filesystemActions'
import { DirectoryBrowserModal } from './DirectoryBrowserModal'

type FilesystemProvider = typeof filesystemActions

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const createDeferred = <T,>() => {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })

  return { promise, resolve, reject }
}

const renderModal = (props: React.ComponentProps<typeof DirectoryBrowserModal>) => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<DirectoryBrowserModal {...props} />)
  })

  return {
    container,
    root,
    rerender(nextProps: React.ComponentProps<typeof DirectoryBrowserModal>) {
      act(() => {
        root.render(<DirectoryBrowserModal {...nextProps} />)
      })
    },
    unmount() {
      act(() => {
        root.unmount()
      })
      container.remove()
    },
  }
}

const defaultProvider: FilesystemProvider = {
  listDirectories: vi.fn(),
  resolveDirectory: vi.fn(),
}

describe('DirectoryBrowserModal', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.clearAllMocks()
  })

  it('loads the initial path and shows current and child directories', async () => {
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn().mockResolvedValue({
        currentPath: 'C:/working/root',
        parentPath: 'C:/working',
        directories: [
          { name: 'alpha', fullPath: 'C:/working/root/alpha', canBrowse: true, hasChildren: false },
          { name: 'beta', fullPath: 'C:/working/root/beta', canBrowse: true, hasChildren: true },
        ],
      } satisfies DirectoryBrowseResult),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect: vi.fn(),
      provider,
    })

    expect(provider.listDirectories).toHaveBeenCalledWith('C:/working/root')

    await flushPromises()

    expect(document.body.textContent).toContain('Current path')
    expect(document.body.textContent).toContain('C:/working/root')
    expect(document.body.textContent).toContain('Parent')
    expect(document.body.textContent).toContain('alpha')
    expect(document.body.textContent).toContain('beta')

    unmount()
  })

  it('navigates into a child directory and reloads that path', async () => {
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn()
        .mockResolvedValueOnce({
          currentPath: 'C:/working/root',
          parentPath: 'C:/working',
          directories: [
            { name: 'alpha', fullPath: 'C:/working/root/alpha', canBrowse: true, hasChildren: false },
          ],
        } satisfies DirectoryBrowseResult)
        .mockResolvedValueOnce({
          currentPath: 'C:/working/root/alpha',
          parentPath: 'C:/working/root',
          directories: [],
        } satisfies DirectoryBrowseResult),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect: vi.fn(),
      provider,
    })

    await flushPromises()

    const alphaButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('alpha')) as HTMLButtonElement | undefined
    expect(alphaButton).toBeTruthy()

    act(() => {
      alphaButton!.click()
    })

    await flushPromises()

    expect(provider.listDirectories).toHaveBeenNthCalledWith(2, 'C:/working/root/alpha')
    expect(document.body.textContent).toContain('C:/working/root/alpha')

    unmount()
  })

  it('returns the current path when selecting the directory', async () => {
    const onSelect = vi.fn()
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn().mockResolvedValue({
        currentPath: 'C:/working/root',
        parentPath: 'C:/working',
        directories: [],
      } satisfies DirectoryBrowseResult),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect,
      provider,
    })

    await flushPromises()

    const selectButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Select This Directory')) as HTMLButtonElement | undefined
    expect(selectButton).toBeTruthy()

    act(() => {
      selectButton!.click()
    })

    expect(onSelect).toHaveBeenCalledWith('C:/working/root')

    unmount()
  })

  it('shows loading and error states', async () => {
    const deferred = createDeferred<DirectoryBrowseResult>()
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn().mockReturnValue(deferred.promise),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect: vi.fn(),
      provider,
    })

    expect(document.body.textContent).toContain('Loading')

    deferred.reject(new Error('filesystem unavailable'))
    await flushPromises()

    expect(document.body.textContent).toContain('filesystem unavailable')

    unmount()
  })

  it('keeps the newest directory view when earlier requests resolve later', async () => {
    const alphaDeferred = createDeferred<DirectoryBrowseResult>()
    const betaDeferred = createDeferred<DirectoryBrowseResult>()
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn()
        .mockResolvedValueOnce({
          currentPath: 'C:/working/root',
          parentPath: 'C:/working',
          directories: [
            { name: 'alpha', fullPath: 'C:/working/root/alpha', canBrowse: true, hasChildren: false },
            { name: 'beta', fullPath: 'C:/working/root/beta', canBrowse: true, hasChildren: false },
          ],
        } satisfies DirectoryBrowseResult)
        .mockReturnValueOnce(alphaDeferred.promise)
        .mockReturnValueOnce(betaDeferred.promise),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect: vi.fn(),
      provider,
    })

    await flushPromises()

    const buttons = Array.from(document.querySelectorAll('button'))
    const alphaButton = buttons.find((button) => button.textContent?.includes('alpha')) as HTMLButtonElement | undefined
    const betaButton = buttons.find((button) => button.textContent?.includes('beta')) as HTMLButtonElement | undefined

    expect(alphaButton).toBeTruthy()
    expect(betaButton).toBeTruthy()

    act(() => {
      alphaButton!.click()
    })

    act(() => {
      betaButton!.click()
    })

    betaDeferred.resolve({
      currentPath: 'C:/working/root/beta',
      parentPath: 'C:/working/root',
      directories: [],
    })
    await flushPromises()

    expect(document.body.textContent).toContain('C:/working/root/beta')

    alphaDeferred.resolve({
      currentPath: 'C:/working/root/alpha',
      parentPath: 'C:/working/root',
      directories: [],
    })
    await flushPromises()

    expect(document.body.textContent).toContain('C:/working/root/beta')
    expect(document.body.textContent).not.toContain('C:/working/root/alpha')

    unmount()
  })

  it('disables selecting the directory after a load error', async () => {
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn().mockRejectedValue(new Error('filesystem unavailable')),
    }

    const { unmount } = renderModal({
      open: true,
      initialPath: 'C:/working/root',
      onCancel: vi.fn(),
      onSelect: vi.fn(),
      provider,
    })

    await flushPromises()

    const selectButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Select This Directory')) as HTMLButtonElement | undefined
    expect(selectButton).toBeTruthy()
    expect(selectButton!.disabled).toBe(true)

    unmount()
  })
})
