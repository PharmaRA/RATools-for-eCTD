import { act, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { ConfigProvider } from 'antd'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FindingLocation } from './FindingLocation'
import { InputRegistration } from './InputRegistration'
import type { ValidationEvidence } from '../../packageValidationActions'
import type { ValidationFinding } from '../../api/generated'

vi.mock('../../browserDownload', () => ({ downloadBlob: vi.fn(), getDownloadFileName: () => 'index.xml' }))

const evidence: ValidationEvidence = { inputId: 'target', comparisonInputId: null, history: [], ruleCatalog: { rules: [] }, historyBaseline: { entries: [] } }
const finding: ValidationFinding = {
  ruleId: 'XML-DTD', checkStatus: 'Fail', severity: 'Error', code: 'XML_INVALID', message: 'Invalid node', expected: null, actual: null, recommendedAction: null,
  location: { sequenceNumber: '0000', logicalPath: '0000/index.xml', backboneRelativePath: 'index.xml', leafId: null, nodePath: '/ectd', fieldPath: null, line: 1, column: 1, files: null },
}
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
const input = (id: string, value: string) => act(() => {
  const element = document.getElementById(id) as HTMLInputElement
  expect(element).not.toBeNull()
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
})
const click = (text: string) => act(() => {
  const button = Array.from(document.querySelectorAll('button')).find(item => item.textContent?.trim() === text)
  expect(button).toBeDefined()
  button!.click()
})

describe('validation review regressions', () => {
  afterEach(() => { for (const cleanup of dispose.splice(0)) cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

  it('withdraws successful cached text when digest revalidation fails', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ logicalPath: '0000/index.xml', sha256: 'old', startLine: 1, lines: ['ORIGINAL_ONLY'], truncated: false }) })
      .mockResolvedValue({ ok: false, status: 409, json: async () => ({ code: 'INPUT_CHANGED', message: 'Input changed after capture' }) })
    vi.stubGlobal('fetch', fetchMock)
    const client = render(<FindingLocation finding={finding} evidence={evidence} sequence="0000" onClose={() => {}} />)
    await waitFor(() => !!document.querySelector('[data-testid="validation-source-text"]'))
    expect(document.body.textContent).toContain('ORIGINAL_ONLY')
    await act(async () => { await client.refetchQueries({ queryKey: ['validation-text'] }) })
    await waitFor(() => document.body.textContent!.includes('Input changed after capture'))
    expect(client.getQueriesData({ queryKey: ['validation-text'] })[0][1]).toMatchObject({ lines: ['ORIGINAL_ONLY'] })
    expect(document.querySelector('[data-testid="validation-source-text"]')).toBeNull()
    expect(document.body.textContent).not.toContain('ORIGINAL_ONLY')
  })

  it('does not let an invisible partial sequence block an existing input ID', async () => {
    const registered = { id: 'known-input', applicationId: 'application', sequenceNumber: '0000', sourceKind: 'Zip', inputDigest: 'digest', createdUtc: '', fileCount: 1, totalBytes: 1, limitsDigest: 'limits' }
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => registered })
    vi.stubGlobal('fetch', fetchMock)
    const onRegistered = vi.fn()
    render(<InputRegistration applicationId="application" title="Register" onRegistered={onRegistered} onClose={() => {}} />)
    input('validation-sequence', '1')
    act(() => { (document.querySelector('input[type="radio"][value="existing"]') as HTMLInputElement).click() })
    await waitFor(() => !!document.getElementById('validation-input-id'))
    input('validation-input-id', 'known-input')
    click('登记并选择')
    await waitFor(() => onRegistered.mock.calls.length === 1)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/package-validation-inputs/known-input')
    expect(onRegistered).toHaveBeenCalledWith(registered)
  })

  it('keeps cached text withdrawn through a failed download retry and revalidates before restoring it', async () => {
    let resolveDownload!: (response: Response) => void
    let resolveText!: (response: Response) => void
    const preview = (sha256: string, line: string) => new Response(JSON.stringify({ logicalPath: '0000/index.xml', sha256, startLine: 1, lines: [line], truncated: false }), { status: 200 })
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(preview('old', 'ORIGINAL_ONLY'))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'INPUT_CHANGED', message: 'Input changed after capture' }), { status: 409 }))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveDownload = resolve }))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveText = resolve }))
    vi.stubGlobal('fetch', fetchMock)
    render(<FindingLocation finding={finding} evidence={evidence} sequence="0000" onClose={() => {}} />)
    await waitFor(() => !!document.querySelector('[data-testid="validation-source-text"]'))
    click('下载定位文件原始字节')
    await waitFor(() => document.body.textContent!.includes('Input changed after capture'))
    expect(document.body.textContent).not.toContain('ORIGINAL_ONLY')
    click('下载定位文件原始字节')
    await waitFor(() => fetchMock.mock.calls.length === 3)
    expect(document.body.textContent).not.toContain('ORIGINAL_ONLY')
    expect(document.body.textContent).toContain('Input changed after capture')
    await act(async () => { resolveDownload(new Response('<original />', { status: 200 })) })
    await waitFor(() => fetchMock.mock.calls.length === 4)
    expect(document.querySelector('[data-testid="validation-source-text"]')).toBeNull()
    await act(async () => { resolveText(preview('revalidated', 'REVALIDATED_ONLY')) })
    await waitFor(() => document.body.textContent!.includes('REVALIDATED_ONLY'))
    expect(document.body.textContent).not.toContain('ORIGINAL_ONLY')
    expect(document.body.textContent).not.toContain('Input changed after capture')
  })

  it('does not restore cached text if a successful download is followed by failed text revalidation', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ logicalPath: '0000/index.xml', sha256: 'old', startLine: 1, lines: ['ORIGINAL_ONLY'], truncated: false }), { status: 200 }))
      .mockResolvedValueOnce(new Response('<original />', { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'INPUT_CHANGED', message: 'Input changed during retry' }), { status: 409 }))
    vi.stubGlobal('fetch', fetchMock)
    render(<FindingLocation finding={finding} evidence={evidence} sequence="0000" onClose={() => {}} />)
    await waitFor(() => !!document.querySelector('[data-testid="validation-source-text"]'))
    click('下载定位文件原始字节')
    await waitFor(() => document.body.textContent!.includes('Input changed during retry'))
    expect(document.querySelector('[data-testid="validation-source-text"]')).toBeNull()
    expect(document.body.textContent).not.toContain('ORIGINAL_ONLY')
  })

  it('sends surrounding scope explicitly instead of opening a same-spelling sequence file', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ logicalPath: '0000/index.xml', sha256: 'outer', startLine: 1, lines: ['OUTSIDE'], truncated: false }) })
    vi.stubGlobal('fetch', fetchMock)
    const outer = { ...finding, code: 'SURROUNDING_INPUT_FILE', location: { ...finding.location, files: [{ sourceId: 'target', logicalPath: '0000/index.xml', isSurrounding: true }] } }
    render(<FindingLocation finding={outer} evidence={evidence} sequence="0000" onClose={() => {}} />)
    await waitFor(() => fetchMock.mock.calls.length > 0)
    const url = new URL(fetchMock.mock.calls[0][0], 'http://localhost')
    expect(url.searchParams.get('surrounding')).toBe('true')
    expect(url.searchParams.get('logicalPath')).toBe('0000/index.xml')
  })
})
