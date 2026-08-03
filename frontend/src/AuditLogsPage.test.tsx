import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const flushPromises = async () => {
  // 路由懒加载：审计页是 React.lazy 分块，首帧需等动态 import 完成。
  // 与 PublishHistoryDetail.test.tsx 同理，每节拍给 5ms 真实延迟以适配 CI runner。
  for (let tick = 0; tick < 5; tick += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 5))
    })
  }
}

const waitForElement = async (getElement: () => HTMLElement | undefined, label: string) => {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises()
    const element = getElement()
    if (element) return element
  }

  throw new Error(`Could not find ${label}`)
}

const renderApp = (initialPath = '/') => {
  window.history.replaceState(null, '', initialPath)

  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<App />)
  })

  return {
    unmount() {
      act(() => {
        root.unmount()
      })
      container.remove()
    },
  }
}

const clickButtonByText = async (text: string) => {
  const button = await waitForElement(
    () => Array.from(document.querySelectorAll('button')).find((candidate) => candidate.textContent?.trim() === text) as HTMLElement | undefined,
    `button with text ${text}`,
  )

  act(() => {
    button.click()
  })
}

const typeIntoInputById = async (inputId: string, value: string) => {
  const input = await waitForElement(
    () => document.getElementById(inputId) as HTMLInputElement | undefined,
    `input ${inputId}`,
  )

  act(() => {
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
    setter?.call(input, value)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

const auditLogPage = {
  page: 1,
  pageSize: 20,
  totalCount: 2,
  items: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      entityType: 'PublishJob',
      entityId: 'job-1',
      action: 'Completed',
      actor: 'system',
      details: 'MatchedPrefixes=none',
      createdUtc: '2026-07-26T10:00:00Z',
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      entityType: 'SequenceValidation',
      entityId: 'app-1:0000',
      action: 'Validated',
      actor: 'system',
      details: null,
      createdUtc: '2026-07-26T09:00:00Z',
    },
  ],
}

const stubFetch = () => {
  const fetchMock = vi.fn().mockImplementation((url: string) => {
    if (url === '/health') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
    }

    if (String(url).startsWith('/api/audit-logs?')) {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(auditLogPage) })
    }

    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('AuditLogsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('loads the first page with the default page size on mount', async () => {
    const fetchMock = stubFetch()

    const { unmount } = renderApp('/audit-logs')
    await flushPromises()

    expect(
      fetchMock.mock.calls.some((call) => String(call[0]) === '/api/audit-logs?page=1&pageSize=20'),
    ).toBe(true)

    unmount()
  })

  it('renders audit entries returned by the API', async () => {
    stubFetch()

    const { unmount } = renderApp('/audit-logs')
    await waitForElement(
      () => Array.from(document.querySelectorAll('td')).find((cell) => cell.textContent?.includes('job-1')) as HTMLElement | undefined,
      'audit row for job-1',
    )

    const bodyText = document.body.textContent ?? ''
    expect(bodyText).toContain('PublishJob')
    expect(bodyText).toContain('SequenceValidation')
    expect(bodyText).toContain('app-1:0000')
    expect(bodyText).toContain('MatchedPrefixes=none')

    unmount()
  })

  it('sends entity id and action filters and resets to the first page', async () => {
    const fetchMock = stubFetch()

    const { unmount } = renderApp('/audit-logs')
    await flushPromises()

    await typeIntoInputById('entityId', 'job-1')
    await typeIntoInputById('action', 'Completed')
    await clickButtonByText('查询')
    await flushPromises()

    expect(
      fetchMock.mock.calls.some((call) => String(call[0]) === '/api/audit-logs?page=1&pageSize=20&entityId=job-1&action=Completed'),
    ).toBe(true)

    unmount()
  })

  it('reaches the audit page from the top bar entry', async () => {
    const fetchMock = stubFetch()

    const { unmount } = renderApp('/')
    await flushPromises()
    await clickButtonByText('审计日志')
    await flushPromises()

    expect(window.location.pathname).toBe('/audit-logs')
    expect(
      fetchMock.mock.calls.some((call) => String(call[0]).startsWith('/api/audit-logs?')),
    ).toBe(true)

    unmount()
  })
})
