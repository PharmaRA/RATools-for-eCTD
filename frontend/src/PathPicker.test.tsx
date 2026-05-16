import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { type DirectoryBrowseResult, type DirectoryResolutionResult, type filesystemActions } from './filesystemActions'
import { PathPicker } from './PathPicker'

type FilesystemProvider = typeof filesystemActions

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const renderPathPicker = (props: React.ComponentProps<typeof PathPicker>) => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<PathPicker {...props} />)
  })

  return {
    container,
    root,
    rerender(nextProps: React.ComponentProps<typeof PathPicker>) {
      act(() => {
        root.render(<PathPicker {...nextProps} />)
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

const setInputValue = (input: HTMLInputElement, value: string) => {
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  valueSetter?.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
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

const defaultProvider: FilesystemProvider = {
  listDirectories: vi.fn(),
  resolveDirectory: vi.fn(),
}

describe('PathPicker', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.clearAllMocks()
  })

  it('normalizes a typed path on blur', async () => {
    const onChange = vi.fn()
    const provider = {
      ...defaultProvider,
      resolveDirectory: vi.fn().mockResolvedValue({
        fullPath: 'C:/working/root/alpha',
        exists: true,
        isDirectory: true,
        isAccessible: true,
      } satisfies DirectoryResolutionResult),
    }

    const { unmount } = renderPathPicker({
      value: 'C:/working/root',
      onChange,
      provider,
    })

    const input = document.querySelector('input') as HTMLInputElement
    expect(input).toBeTruthy()

    act(() => {
      setInputValue(input, 'C:/working/root/./alpha')
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
    })

    await flushPromises()

    expect(provider.resolveDirectory).toHaveBeenCalledWith('C:/working/root/./alpha')
    expect(onChange).toHaveBeenCalledWith('C:/working/root/alpha')
    expect((document.querySelector('input') as HTMLInputElement).value).toBe('C:/working/root/alpha')

    unmount()
  })

  it('reports typed path changes before blur so forms can submit the draft value', () => {
    const onChange = vi.fn()

    const { unmount } = renderPathPicker({
      value: '',
      onChange,
      provider: defaultProvider,
    })

    const input = document.querySelector('input') as HTMLInputElement

    act(() => {
      setInputValue(input, 'C:/working/root')
    })

    expect(onChange).toHaveBeenCalledWith('C:/working/root')

    unmount()
  })

  it('shows validation feedback when a typed path cannot be resolved', async () => {
    const provider = {
      ...defaultProvider,
      resolveDirectory: vi.fn().mockRejectedValue(new Error('Directory not found')),
    }

    const { unmount } = renderPathPicker({
      value: 'C:/working/root',
      onChange: vi.fn(),
      provider,
    })

    const input = document.querySelector('input') as HTMLInputElement

    act(() => {
      setInputValue(input, 'C:/working/missing')
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
    })

    await flushPromises()

    expect(document.body.textContent).toContain('Directory path could not be resolved')
    expect(document.body.textContent).toContain('Directory not found')

    unmount()
  })

  it('shows a clearer message when the local API cannot be reached', async () => {
    const provider = {
      ...defaultProvider,
      resolveDirectory: vi.fn().mockRejectedValue(new TypeError('Failed to fetch')),
    }

    const { unmount } = renderPathPicker({
      value: 'E:/Temp',
      onChange: vi.fn(),
      provider,
    })

    const input = document.querySelector('input') as HTMLInputElement

    act(() => {
      setInputValue(input, 'E:/Temp/ratools-workspaces')
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
    })

    await flushPromises()

    expect(document.body.textContent).toContain('Directory path could not be resolved')
    expect(document.body.textContent).toContain('Cannot reach the API server. If you are running locally, make sure the backend is running at http://localhost:5000.')

    unmount()
  })

  it('opens the directory browser when Browse is clicked', async () => {
    const provider = {
      ...defaultProvider,
      listDirectories: vi.fn().mockResolvedValue({
        currentPath: 'C:/working/root',
        parentPath: 'C:/working',
        directories: [],
      } satisfies DirectoryBrowseResult),
    }

    const { unmount } = renderPathPicker({
      value: 'C:/working/root',
      onChange: vi.fn(),
      provider,
    })

    const browseButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Browse')) as HTMLButtonElement | undefined
    expect(browseButton).toBeTruthy()

    act(() => {
      browseButton!.click()
    })

    await flushPromises()

    expect(provider.listDirectories).toHaveBeenCalledWith('C:/working/root')
    expect(document.body.textContent).toContain('Choose Directory')

    unmount()
  })

  it('writes the selected directory back into the field', async () => {
    const onChange = vi.fn()
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

    const { unmount } = renderPathPicker({
      value: 'C:/working/root',
      onChange,
      provider,
    })

    const browseButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Browse')) as HTMLButtonElement | undefined

    act(() => {
      browseButton!.click()
    })

    await flushPromises()

    const alphaButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('alpha')) as HTMLButtonElement | undefined

    act(() => {
      alphaButton!.click()
    })

    await flushPromises()

    const selectButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Select This Directory')) as HTMLButtonElement | undefined

    act(() => {
      selectButton!.click()
    })

    expect(onChange).toHaveBeenCalledWith('C:/working/root/alpha')
    expect((document.querySelector('input') as HTMLInputElement).value).toBe('C:/working/root/alpha')

    unmount()
  })

  it('keeps the browse selection when a slower blur normalization resolves later', async () => {
    const onChange = vi.fn()
    const normalizeDeferred = createDeferred<DirectoryResolutionResult>()
    const provider = {
      ...defaultProvider,
      resolveDirectory: vi.fn().mockReturnValue(normalizeDeferred.promise),
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

    const { unmount } = renderPathPicker({
      value: 'C:/working/root',
      onChange,
      provider,
    })

    const input = document.querySelector('input') as HTMLInputElement
    act(() => {
      setInputValue(input, 'C:/working/root/typed')
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
    })

    const browseButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Browse')) as HTMLButtonElement | undefined
    act(() => {
      browseButton!.click()
    })

    await flushPromises()

    const alphaButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('alpha')) as HTMLButtonElement | undefined
    act(() => {
      alphaButton!.click()
    })

    await flushPromises()

    const selectButton = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Select This Directory')) as HTMLButtonElement | undefined
    act(() => {
      selectButton!.click()
    })

    expect(onChange).toHaveBeenCalledWith('C:/working/root/typed')
    expect(onChange).toHaveBeenCalledWith('C:/working/root/alpha')

    normalizeDeferred.resolve({
      fullPath: 'C:/working/root/typed',
      exists: true,
      isDirectory: true,
      isAccessible: true,
    })
    await flushPromises()

    expect(onChange).toHaveBeenLastCalledWith('C:/working/root/alpha')
    expect((document.querySelector('input') as HTMLInputElement).value).toBe('C:/working/root/alpha')

    unmount()
  })
})
