import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const renderApp = () => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<App />)
  })

  return {
    container,
    root,
    unmount() {
      act(() => {
        root.unmount()
      })
      container.remove()
    },
  }
}

const clickByText = (text: string) => {
  const element = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes(text)) as HTMLButtonElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

const clickPrimaryModalButton = () => {
  const element = Array.from(document.querySelectorAll('.ant-modal .ant-btn-primary')).at(-1) as HTMLButtonElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia
}

if (!globalThis.ResizeObserver) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }

  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

const setInputValue = (input: HTMLInputElement, value: string) => {
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  valueSetter?.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

const getInputByPlaceholder = (placeholder: string) => {
  const input = Array.from(document.querySelectorAll('input')).find((element) => element.placeholder === placeholder) as HTMLInputElement | undefined
  expect(input).toBeTruthy()
  return input!
}

const ectdTemplatesResponse = [
  { key: 'us-fda-ectd-3.2.2', displayName: 'US FDA eCTD 3.2.2' },
]

describe('PathPicker form hosts', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('submits create application with workingDirectoryParentPath', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string, options?: RequestInit) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/ectd-templates') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(ectdTemplatesResponse) })
      }

      if (url === '/api/applications') {
        if (options?.method === 'POST') {
          return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ id: 'app-1' }) })
        }
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()

    clickByText('New Application')

    const applicationNumberInput = getInputByPlaceholder('e.g. NDA123456')
    const sponsorInput = getInputByPlaceholder('e.g. Acme Pharma Ltd.')
    const pathInput = getInputByPlaceholder('e.g. C:/eCTD/workspaces')

    act(() => {
      setInputValue(applicationNumberInput, 'APP-1')
      setInputValue(sponsorInput, 'Demo Sponsor')
      setInputValue(pathInput, 'C:/working/root')
    })

    await flushPromises()

    clickPrimaryModalButton()

    await flushPromises()

    const calls = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls as Array<[string, RequestInit?]>
    const createCall = calls.find(([url, options]) => url === '/api/applications' && options?.method === 'POST')
    expect(createCall).toBeTruthy()
    expect(JSON.parse(String(createCall?.[1]?.body))).toMatchObject({
      applicationNumber: 'APP-1',
      sponsorName: 'Demo Sponsor',
      workingDirectoryParentPath: 'C:/working/root',
    })

    unmount()
  })

  it('submits import application with workingDirectoryPath', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string, options?: RequestInit) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/ectd-templates') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(ectdTemplatesResponse) })
      }

      if (url === '/api/applications/import') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ importedSequenceCount: 0, importedDocumentCount: 0, importedPlacementCount: 0, skippedSequenceCount: 0, failedSequenceCount: 0, issues: [] }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()

    clickByText('Import Application')

    const pathInput = getInputByPlaceholder('e.g. C:/eCTD/workspaces/NDA123456')
    const sponsorInput = getInputByPlaceholder('e.g. Demo Sponsor')

    act(() => {
      setInputValue(pathInput, 'D:/workspace/import')
      setInputValue(sponsorInput, 'Imported Sponsor')
    })

    await flushPromises()

    clickPrimaryModalButton()

    await flushPromises()

    const calls = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls as Array<[string, RequestInit?]>
    const importCall = calls.find(([url]) => url === '/api/applications/import')
    expect(importCall).toBeTruthy()
    expect(JSON.parse(String(importCall?.[1]?.body))).toMatchObject({
      workingDirectoryPath: 'D:/workspace/import',
      sponsorName: 'Imported Sponsor',
    })

    unmount()
  })

  it('submits publish sequence with outputDirectoryPath', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string, options?: RequestInit) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/applications') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'app-1',
            applicationNumber: 'APP-1',
            sponsorName: 'Sponsor',
            region: 'US',
            createdUtc: '2024-01-01T00:00:00Z',
            sequences: [
              { sequenceNumber: '0000', submissionType: 'Original Application', description: 'Desc' },
            ],
          },
        ]) })
      }

      if (url === '/api/publish-jobs') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({}) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()

    clickByText('Manage App')
    await flushPromises()
    clickByText('Enter Workspace')
    await flushPromises()
    clickByText('Publish Sequence')

    const input = getInputByPlaceholder('e.g. C:/eCTD/exports')

    act(() => {
      setInputValue(input, 'E:/exports/submission-a')
    })

    await flushPromises()

    clickPrimaryModalButton()

    await flushPromises()

    const calls = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls as Array<[string, RequestInit?]>
    const publishCall = calls.find(([url, options]) => url === '/api/publish-jobs' && options?.method === 'POST')
    expect(publishCall).toBeTruthy()
    expect(JSON.parse(String(publishCall?.[1]?.body))).toMatchObject({
      outputDirectoryPath: 'E:/exports/submission-a',
    })

    unmount()
  })
})
