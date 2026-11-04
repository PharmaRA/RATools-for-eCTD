import { act, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { ConfigProvider } from 'antd'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ExternalComparisonPanel } from './ExternalComparisonPanel'

const dispose: Array<() => void> = []
const render = (view: ReactNode, client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  act(() => root.render(<ConfigProvider button={{ autoInsertSpace: false }}><QueryClientProvider client={client}>{view}</QueryClientProvider></ConfigProvider>))
  dispose.push(() => { act(() => root.unmount()); container.remove(); client.clear() })
  return client
}
const waitFor = async (predicate: () => boolean) => {
  for (let attempt = 0; attempt < 100; attempt++) {
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
    if (predicate()) return
  }
  throw new Error('Expected UI state did not appear')
}
const click = (text: string) => act(() => {
  const button = Array.from(document.querySelectorAll('button')).find(item => item.textContent?.trim() === text)
  expect(button).toBeDefined()
  button!.click()
})

describe('external comparison evidence panel', () => {
  afterEach(() => { for (const cleanup of dispose.splice(0)) cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

  it.each([401, 422, 500])('shows a %s error without claiming evidence is absent', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: 'evidence unavailable' }), { status })))
    render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.body.textContent!.includes('evidence unavailable'))
    expect(document.body.textContent).not.toContain('此运行尚无外部对照证据')
    expect(document.body.textContent).not.toContain('登记外部对照证据')
  })

  it('retains tool and version fields after a registration failure', async () => {
    let rejectRegistration!: (response: Response) => void
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response('{}', { status: 404 }))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { rejectRegistration = resolve })))
    render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.querySelector('#toolName') !== null)
    act(() => {
      for (const [selector, value] of [['#toolName', 'My validator'], ['#toolVersion', '22.1'], ['textarea', 'report body']]) {
        const element = document.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement
        const prototype = element.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
        Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value)
        element.dispatchEvent(new Event('input', { bubbles: true }))
      }
    })
    click('登记外部对照证据')
    await waitFor(() => rejectRegistration !== undefined)
    await act(async () => { rejectRegistration(new Response(JSON.stringify({ message: 'registration rejected' }), { status: 422 })) })
    await waitFor(() => document.body.textContent!.includes('registration rejected'))
    expect((document.querySelector('#toolName') as HTMLInputElement).value).toBe('My validator')
    expect((document.querySelector('#toolVersion') as HTMLInputElement).value).toBe('22.1')
  })

  it.each(['metadata', 'text'])('withdraws cached evidence on a failed %s refresh and verifies both on retry', async failedPart => {
    const evidence = { runId: 'run', toolName: 'validator', toolVersion: '1', packageDigest: 'a'.repeat(64),
      reportSha256: 'b'.repeat(64), recordedUtc: '2026-10-18T00:00:00Z', digest: 'c'.repeat(64), differences: [] }
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(evidence)))
      .mockResolvedValueOnce(new Response('original report'))
    vi.stubGlobal('fetch', fetch)
    const client = render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.querySelector('[data-testid="external-report-text"]') !== null)
    fetch.mockResolvedValueOnce(new Response(JSON.stringify({ message: 'integrity failure' }), { status: 422 }))
    await act(async () => { await client.refetchQueries({ queryKey: [failedPart === 'metadata' ? 'package-validation-external' : 'package-validation-external-text', 'run'] }) })
    await waitFor(() => document.body.textContent!.includes('integrity failure'))
    expect(document.querySelector('[data-testid="external-report-text"]')).toBeNull()
    let finishText!: (value: Response) => void
    fetch.mockResolvedValueOnce(new Response(JSON.stringify(evidence)))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finishText = resolve }))
    click('重新读取外部证据')
    await waitFor(() => finishText !== undefined)
    expect(document.querySelector('[data-testid="external-report-text"]')).toBeNull()
    await act(async () => { finishText(new Response('verified report')) })
    await waitFor(() => document.querySelector('[data-testid="external-report-text"]')?.textContent === 'verified report')
  })

  it('shows the registered tool report and differences without inferring evidence', async () => {
    const evidence = { runId: 'run', toolName: 'eValidator', toolVersion: '22.1', packageDigest: 'a'.repeat(64),
      reportSha256: 'b'.repeat(64), recordedUtc: '2026-10-18T00:00:00Z', digest: 'c'.repeat(64),
      differences: [{ externalRuleId: 'EXT-1', internalRuleId: 'FILE-HREF', externalMessage: 'Missing href', explanation: 'Same payload.' }] }
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(evidence), { status: 200 }))
      .mockResolvedValueOnce(new Response('<external-report />', { status: 200 })))
    render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.body.textContent!.includes('eValidator'))
    expect(document.body.textContent).toContain('Same payload.')
    // The registration form is not offered once evidence exists.
    expect(Array.from(document.querySelectorAll('button')).some(button => button.textContent?.trim() === '登记外部对照证据')).toBe(false)
  })

  it('keeps the form and reports that evidence is missing on 404', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: 'not found' }), { status: 404 })))
    render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.body.textContent!.includes('此运行尚无外部对照证据'))
    expect(document.body.textContent).toContain('登记外部对照证据')
  })

  it('requires an explanation before a difference can be added', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: 'not found' }), { status: 404 })))
    render(<ExternalComparisonPanel runId="run" packageDigest={'a'.repeat(64)} />)
    await waitFor(() => document.body.textContent!.includes('此运行尚无外部对照证据'))
    click('添加差异')
    await waitFor(() => document.body.textContent!.includes('每条差异都需要外部规则 ID、外部结论和解释。'))
  })
})
