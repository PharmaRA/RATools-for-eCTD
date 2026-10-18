import { act, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { ConfigProvider } from 'antd'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DossierProjectionPanel } from './DossierProjectionPanel'
import type { ValidationProjectionPage } from '../../packageValidationActions'

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
const address = (sequenceNumber: string, leafId: string) => ({ applicationId: 'app', sequenceNumber, backboneRelativePath: 'index.xml', leafId })
const page = (overrides: Partial<ValidationProjectionPage> = {}): ValidationProjectionPage => ({
  schemaVersion: 1, view: 'current', sequenceNumber: null, applicationId: 'app', targetSequence: '0001',
  profileSnapshotId: 'ich', inputDigest: 'a'.repeat(64), baselineDigest: 'b'.repeat(64), engineVersion: 'package-delivery-v3;dossier-projection-v1',
  isComplete: true, incompleteReasons: [], appliedSequences: ['0000', '0001'], baseline: [], totalCount: 2, offset: 0, limit: 50,
  leaves: [
    { address: address('0000', 'alpha-spec-v1'), title: 'Alpha', operation: 'new', contentLogicalPath: '0000/index.xml',
      status: 'Replaced', replacedBy: '0001/index.xml#alpha-spec-v2', deletedBy: null, appendedTo: null, isRegionalReference: false, sourceSequence: '0000' },
    { address: address('0001', 'alpha-spec-v2'), title: 'Alpha', operation: 'replace', contentLogicalPath: '0001/index.xml',
      status: 'Current', replacedBy: null, deletedBy: null, appendedTo: null, isRegionalReference: false, sourceSequence: '0001' },
  ],
  events: [], findings: [], ...overrides,
})
const respond = (body: ValidationProjectionPage) => vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))

describe('dossier projection panel', () => {
  afterEach(() => { for (const cleanup of dispose.splice(0)) cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

  it('renders the archived current view with explicit replacement history', async () => {
    const fetchMock = respond(page())
    vi.stubGlobal('fetch', fetchMock)
    render(<DossierProjectionPanel runId="run" targetSequence="0001" />)
    await waitFor(() => document.body.textContent!.includes('alpha-spec-v2'))
    expect(fetchMock.mock.calls[0][0]).toBe('/api/package-validations/run/projection?view=current&offset=0&limit=50')
    expect(document.body.textContent).toContain('当前有效')
    expect(document.body.textContent).toContain('已被替换')
    expect(document.body.textContent).toContain('被 0001/index.xml#alpha-spec-v2 替换')
  })

  it('states incompleteness instead of presenting unresolved history as effective', async () => {
    vi.stubGlobal('fetch', respond(page({ isComplete: false, incompleteReasons: ['0001: no readable backbone was inspected.'] })))
    render(<DossierProjectionPanel runId="run" targetSequence="0001" />)
    await waitFor(() => document.body.textContent!.includes('此累计视图不完整'))
    expect(document.body.textContent).toContain('0001: no readable backbone was inspected.')
    expect(document.body.textContent).toContain('不完整')
  })

  it('requests the three views explicitly and never invents a sequence', async () => {
    const fetchMock = respond(page({ view: 'sequence', sequenceNumber: '0001', leaves: [] }))
    vi.stubGlobal('fetch', fetchMock)
    render(<DossierProjectionPanel runId="run" targetSequence="0001" />)
    await waitFor(() => fetchMock.mock.calls.length === 1)
    const history = Array.from(document.querySelectorAll('input[type="radio"]')).find(item =>
      (item as HTMLInputElement).value === 'history') as HTMLInputElement | undefined
    expect(history).toBeDefined()
    await act(async () => { history!.click() })
    await waitFor(() => fetchMock.mock.calls.length === 2)
    expect(fetchMock.mock.calls[1][0]).toBe('/api/package-validations/run/projection?view=history&offset=0&limit=50')
  })
})
