import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('antd', async (importOriginal) => {
  const actual = await importOriginal<typeof import('antd')>()

  return {
    ...actual,
    message: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
      loading: vi.fn(),
    },
  }
})

import App from './App'
import { messages } from './i18n/messages'

const flushPromises = async () => {
  // 路由懒加载后首次渲染需等待动态 import + Suspense 重渲染，
  // 多跑几个宏任务节拍确保链式异步（加载→挂载→数据请求）都落地。
  for (let tick = 0; tick < 5; tick += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 5))
    })
  }
}

const waitFor = async (assertion: () => void) => {
  let lastError: unknown

  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      assertion()
      return
    } catch (error) {
      lastError = error
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 0))
      })
    }
  }

  throw lastError
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

const clickByText = async (text: string) => {
  // 懒加载路由下按钮出现的时机取决于动态 import 完成，带重试等待。
  let element: HTMLButtonElement | undefined
  for (let attempt = 0; attempt < 40 && !element; attempt += 1) {
    element = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes(text)) as HTMLButtonElement | undefined
    if (!element) {
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 5))
      })
    }
  }
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

    await clickByText('新建申请')

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
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/ectd-templates') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(ectdTemplatesResponse) })
      }

      if (url === '/api/applications/import') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
          applicationId: 'app-imported',
          applicationNumber: 'IND-IMPORT',
          workingDirectoryPath: 'D:/workspace/import',
          importedSequenceCount: 2,
          importedDocumentCount: 3,
          importedPlacementCount: 3,
          skippedSequenceCount: 1,
          failedSequenceCount: 0,
          issues: [
            { severity: 'Warning', code: 'LIFECYCLE_TARGET_MISSING', sequenceNumber: '0002', message: 'Lifecycle leaf is missing modified-file.' },
            { severity: 'Warning', code: 'LIFECYCLE_TARGET_NOT_IMPORTED', sequenceNumber: '0003', message: 'modified-file was not imported.' },
            { severity: 'Warning', code: 'SEQUENCE_INDEX_MISSING', sequenceNumber: '0004', message: 'Sequence index.xml missing.' },
          ],
        }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()

    await clickByText('导入申请')

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
    expect(document.querySelector('[data-testid="import-result-summary"]')?.textContent).toContain(`3 ${messages.importResult.totalLabel}`)
    expect(document.querySelector('[data-testid="import-result-summary"]')?.textContent).toContain(`3 ${messages.importResult.warningLabel}`)
    expect(document.querySelector('[data-testid="import-result-summary"]')?.textContent).toContain(`0 ${messages.importResult.errorLabel}`)
    expect(document.querySelector('[data-testid="import-result-summary"]')?.textContent).toContain(`2 ${messages.importResult.lifecycleWarningLabel}`)
    expect(document.querySelector('[data-testid="import-result-lifecycle-issues"]')?.textContent).toContain('生命周期目标需审阅')
    expect(document.querySelector('[data-testid="import-result-lifecycle-issues"]')?.textContent).toContain('LIFECYCLE_TARGET_MISSING')
    expect(document.querySelector('[data-testid="import-result-lifecycle-issues"]')?.textContent).toContain('LIFECYCLE_TARGET_NOT_IMPORTED')
    expect(document.querySelector('[data-testid="import-result-other-issues"]')?.textContent).toContain('其他导入问题')
    expect(document.querySelector('[data-testid="import-result-other-issues"]')?.textContent).toContain('SEQUENCE_INDEX_MISSING')
    expect(document.querySelector('[data-testid="import-result-other-issues"]')?.textContent).not.toContain('LIFECYCLE_TARGET_MISSING')
    expect(document.querySelector('[data-testid="import-result-all-issues"]')?.textContent).toContain('全部导入问题')
    expect(document.querySelector('[data-testid="import-result-all-issues"]')?.textContent).toContain('modified-file was not imported.')

    unmount()
  })

  it('validates then submits publish sequence without a client output path', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/applications') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'app-1',
            applicationNumber: 'APP-1',
            sponsorName: 'Sponsor',
            ectdTemplateKey: 'us-fda-ectd-3.2.2',
            ectdTemplateDisplayName: 'US FDA eCTD 3.2.2',
            createdUtc: '2024-01-01T00:00:00Z',
            sequences: [
              { sequenceNumber: '0000', submissionType: 'Original Application', description: 'Desc' },
            ],
          },
        ]) })
      }

      if (url === '/api/applications/app-1') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
            id: 'app-1',
            applicationNumber: 'APP-1',
            sponsorName: 'Sponsor',
            ectdTemplateKey: 'us-fda-ectd-3.2.2',
            ectdTemplateDisplayName: 'US FDA eCTD 3.2.2',
            createdUtc: '2024-01-01T00:00:00Z',
            sequences: [
              { sequenceNumber: '0000', submissionType: 'Original Application', description: 'Desc' },
            ],
          }) })
      }

      if (url === '/api/validation/sequence') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({
            applicationId: 'app-1',
            sequenceNumber: '0000',
            validationProfile: 'US FDA eCTD 3.2.2',
            isValid: true,
            issues: [],
            sectionMatches: [],
            lifecycleMatches: [],
          }),
        })
      }

      if (url === '/api/validation/publish-readiness') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({
            applicationId: 'app-1',
            sequenceNumber: '0000',
            isReady: true,
            status: 'Ready',
            blockingErrorCount: 0,
            warningCount: 0,
            validationReport: {
              applicationId: 'app-1',
              sequenceNumber: '0000',
              validationProfile: 'US FDA eCTD 3.2.2',
              isValid: true,
              issues: [],
              sectionMatches: [],
              lifecycleMatches: [],
            },
            missingMetadataFields: [],
            categorySummaries: [],
            findings: [],
          }),
        })
      }

      if (url === '/api/publish-jobs/execute') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({}) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()

    await clickByText('管理')
    await flushPromises()
    await clickByText('进入工作区')
    await flushPromises()
    await clickByText('发布序列')

    await waitFor(() => {
      expect(document.querySelector('.ant-modal')?.textContent).toContain('发布序列')
    })

    clickPrimaryModalButton()

    await flushPromises()

    const calls = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls as Array<[string, RequestInit?]>
    const validationCall = calls.find(([url, options]) => url === '/api/validation/sequence' && options?.method === 'POST')
    expect(validationCall).toBeTruthy()
    expect(JSON.parse(String(validationCall?.[1]?.body))).toMatchObject({
      applicationId: 'app-1',
      sequenceNumber: '0000',
    })

    const publishCall = calls.find(([url, options]) => url === '/api/publish-jobs/execute' && options?.method === 'POST')
    expect(publishCall).toBeTruthy()
    expect(JSON.parse(String(publishCall?.[1]?.body))).toMatchObject({
      applicationId: 'app-1',
      sequenceNumber: '0000',
    })

    unmount()
  })
})
