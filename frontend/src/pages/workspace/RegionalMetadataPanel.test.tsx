import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { RegionalMetadataPanel } from './RegionalMetadataPanel'

const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.unstubAllGlobals() })
const descriptor = (key: string, kind = 'text', fields: unknown[] = []) => ({ key, label: key, kind, fields, options: [], required: false, conditions: [] })
const schema = { schemaVersion: 1, regionKey: 'EU', standardsSnapshotId: 'sha256:pinned', qualificationStatus: 'RegionalQualificationPending', sources: [],
  sequenceFields: [descriptor('eu', 'object', [descriptor('envelopes', 'collection', [descriptor('applicant')])])],
  defaultsFields: [descriptor('eu', 'object', [descriptor('applicant')])], nodeContextFields: [descriptor('country')], documentContextFields: [descriptor('country')] }
const content = { schemaVersion: 1, regionKey: 'EU', eu: { envelopes: [{ id: 'envelope-a', applicant: 'Original' }] } }
const revision = { id: 'metadata-1', applicationId: 'app', sequenceNumber: '0001', metadataRevision: 1, workspaceRevision: 4,
  standardsSnapshotId: 'sha256:pinned', content, nodeContexts: [], documentContexts: [], provenance: [], diagnostics: [], digest: 'digest-1' }
const metadata = { applicationId: 'app', sequenceNumber: '0001', workspaceRevision: 4, standardsProfile: 'EU', submissionType: 'initial',
  sequenceDescription: 'Shared description', applicantName: 'Shared applicant', regional: revision }
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } })
const click = async (text: string) => act(async () => {
  const button = Array.from(document.querySelectorAll('button')).find(item => item.textContent === text)
  if (!button) throw new Error('Missing button ' + text)
  button.click(); await new Promise(resolve => setTimeout(resolve, 0))
})
const edit = (path: string, value: string) => act(() => {
  const input = document.querySelector<HTMLInputElement>(`input[aria-label="${path}"]`)!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})
function render(onChanged = vi.fn()) {
  const container = document.createElement('div'); document.body.appendChild(container); const root = createRoot(container)
  const draw = (workspaceRevision: number) => act(() => root.render(<RegionalMetadataPanel appId="app" seqNumber="0001" workspaceRevision={workspaceRevision}
    disabled={false} nodes={[{ id: 'node-a', label: 'Node A' }, { id: 'node-b', label: 'Node B' }]}
    placements={[{ id: 'placement-a', label: 'A.pdf', nodeInstanceId: 'node-a' }, { id: 'placement-b', label: 'B.pdf', nodeInstanceId: 'node-b' }]} onChanged={onChanged} />))
  draw(4); disposers.push(() => { act(() => root.unmount()); container.remove() }); return { draw, onChanged }
}
function fetchWith(extra?: (url: string, init?: RequestInit) => Response | undefined) {
  const fetch = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input), special = extra?.(url, init); if (special) return special
    if (url.endsWith('/schema?standardsSnapshotId=sha256%3Apinned')) return json(schema)
    if (url.endsWith('/defaults')) return json(null)
    if (url.endsWith('/publishing-metadata')) return json(metadata)
    throw new Error('Unexpected request ' + url)
  }); vi.stubGlobal('fetch', fetch); return fetch
}

it('loads only on demand and saves ordered regional input alongside unchanged shared values at the loaded revision', async () => {
  let saved: Record<string, unknown> | undefined
  fetchWith((url, init) => {
    if (url.endsWith('/publishing-metadata') && init?.method === 'PUT') {
      saved = JSON.parse(String(init.body)); return json({ ...metadata, workspaceRevision: 5, regional: { ...revision, content: (saved!.regional as { content: unknown }).content, workspaceRevision: 5 } })
    }
  })
  const { onChanged } = render(); expect(fetch).not.toHaveBeenCalled()
  await click('读取区域元数据'); edit('eu.envelopes[0].applicant', 'Explicit applicant'); await click('保存区域草稿')
  expect(saved).toMatchObject({ expectedRevision: 4, submissionType: 'initial', applicantName: 'Shared applicant', regional: {
    standardsSnapshotId: 'sha256:pinned', content: { eu: { envelopes: [{ id: 'envelope-a', applicant: 'Explicit applicant' }] } } } })
  expect(onChanged).toHaveBeenCalledOnce()
})

it('retains unsaved values when the workspace revision changes and blocks stale saves', async () => {
  const fetch = fetchWith(); const { draw } = render(); await click('读取区域元数据')
  edit('eu.envelopes[0].applicant', 'Unsaved'); draw(5)
  expect(document.querySelector<HTMLInputElement>('input[aria-label="eu.envelopes[0].applicant"]')!.value).toBe('Unsaved')
  const save = Array.from(document.querySelectorAll('button')).find(item => item.textContent === '保存区域草稿')!
  expect(save.disabled).toBe(true); expect(document.body.textContent).toContain('工作区修订已变化')
  expect(fetch.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(0)
})

it('preflights unsaved input without saving and locates a precise diagnostic field', async () => {
  let received: unknown
  const fetch = fetchWith((url, init) => {
    if (url.endsWith('/regional/preflight')) { received = JSON.parse(String(init?.body)); return json({ standardsSnapshotId: 'sha256:pinned',
      diagnostics: [{ code: 'Required', fieldPath: 'eu.envelopes[0].applicant', message: 'Applicant needed' }], summary: [{ fieldPath: 'eu.envelopes[0].applicant', value: '' }] }) }
  })
  render(); await click('读取区域元数据'); edit('eu.envelopes[0].applicant', ''); await click('预检当前输入')
  expect(received).toMatchObject({ content: { eu: { envelopes: [{ id: 'envelope-a', applicant: null }] } } })
  await click('eu.envelopes[0].applicant')
  expect(document.activeElement?.getAttribute('aria-label')).toBe('eu.envelopes[0].applicant')
  expect(fetch.mock.calls.every(call => call[1]?.method !== 'PUT')).toBe(true)
})

it('shows exact defaults differences before explicitly inheriting the selected immutable revision', async () => {
  const fetch = fetchWith((url, init) => {
    if (url.endsWith('/defaults')) return json({ ...revision, id: 'defaults-7', sequenceNumber: null, metadataRevision: 7, defaults: { schemaVersion: 1, regionKey: 'EU', eu: { applicant: 'Inherited applicant' } } })
    if (url.endsWith('/inheritance/preview')) return json({ sourceRevisionId: 'defaults-7', sourceDigest: 'source-digest', expectedRevision: 4,
      proposedContent: content, nodeContexts: [], documentContexts: [], fieldDiffs: [{ fieldPath: 'eu.envelopes[0].applicant', before: 'Original', after: 'Inherited applicant' }] })
    if (url.endsWith('/inheritance') && init?.method === 'POST') return json({ ...revision, workspaceRevision: 5 })
  })
  render(); await click('读取区域元数据'); await click('预览申请默认值继承')
  expect(document.body.textContent).toContain('Inherited applicant')
  expect(fetch.mock.calls.some(call => String(call[0]).endsWith('/inheritance'))).toBe(false)
  await click('确认采用以上差异')
  const call = fetch.mock.calls.find(call => String(call[0]).endsWith('/inheritance'))!
  expect(JSON.parse(String(call[1]?.body))).toEqual({ expectedRevision: 4, defaultsRevisionId: 'defaults-7' })
})

it('preserves entered data after a conflict and does not report the rejected save as success', async () => {
  fetchWith((url, init) => url.endsWith('/publishing-metadata') && init?.method === 'PUT'
    ? new Response(JSON.stringify({ message: 'Revision conflict' }), { status: 409 }) : undefined)
  const { onChanged } = render(); await click('读取区域元数据'); edit('eu.envelopes[0].applicant', 'Keep after conflict'); await click('保存区域草稿')
  expect(document.body.textContent).toContain('Revision conflict')
  expect(document.querySelector<HTMLInputElement>('input[aria-label="eu.envelopes[0].applicant"]')!.value).toBe('Keep after conflict')
  expect(onChanged).not.toHaveBeenCalled()
})

it('allows an empty defaults draft when the API explicitly reports that no defaults head exists', async () => {
  fetchWith(url => url.endsWith('/defaults') ? new Response(JSON.stringify({ message: 'No defaults yet' }), { status: 404 }) : undefined)
  render(); await click('读取区域元数据'); await click('申请可复用默认值')
  expect(document.querySelector<HTMLInputElement>('input[aria-label="eu.applicant"]')!.value).toBe('')
  expect(document.querySelector('[role="alert"]')).toBeNull()
})

it('clears previous target placements when the explicit node-copy source changes', async () => {
  fetchWith(); render(); await click('读取区域元数据')
  const selector = document.querySelector<HTMLSelectElement>('select[aria-label="节点上下文来源节点"]')!
  act(() => { selector.value = 'node-a'; selector.dispatchEvent(new Event('change', { bubbles: true })) })
  const checkbox = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')).find(input => input.parentElement?.textContent === 'A.pdf')!
  act(() => checkbox.click()); expect(checkbox.checked).toBe(true)
  act(() => { selector.value = 'node-b'; selector.dispatchEvent(new Event('change', { bubbles: true })) })
  expect(checkbox.checked).toBe(false)
  const preview = Array.from(document.querySelectorAll('button')).find(button => button.textContent === '预览节点上下文复制')!
  expect(preview.disabled).toBe(true)
})

it('uses the exact historical defaults snapshot for first inheritance before a regional head exists', async () => {
  const oldSnapshot = 'sha256:previous', currentSnapshot = 'sha256:upgraded'
  const usContent = { schemaVersion: 1, regionKey: 'US', us: { applicantName: 'Historical default applicant' } }
  const usSchema = { ...schema, regionKey: 'US', sequenceFields: [descriptor('us', 'object', [descriptor('applicantName')])],
    defaultsFields: [descriptor('us', 'object', [descriptor('applicantName')])] }
  const fetch = fetchWith((url, init) => {
    if (url.endsWith('/publishing-metadata')) return json({ ...metadata, regional: null })
    if (url.endsWith('/defaults')) return json({ ...revision, id: 'historical-defaults', sequenceNumber: null, standardsSnapshotId: oldSnapshot,
      defaults: usContent })
    if (url.includes('/schema')) return json({ ...usSchema, standardsSnapshotId: url.endsWith(encodeURIComponent(oldSnapshot)) ? oldSnapshot : currentSnapshot })
    if (url.endsWith('/inheritance/preview')) return json({ sourceRevisionId: 'historical-defaults', sourceDigest: 'historical-digest', expectedRevision: 4,
      proposedContent: usContent, nodeContexts: [], documentContexts: [], fieldDiffs: [] })
    if (url.endsWith('/inheritance') && init?.method === 'POST') return json({ ...revision, workspaceRevision: 5, standardsSnapshotId: oldSnapshot, content: usContent })
  })
  const { onChanged, draw } = render(); await click('读取区域元数据'); await click('预览申请默认值继承'); await click('确认采用以上差异'); draw(5)
  expect(document.querySelector('[role="alert"]')).toBeNull()
  expect(document.querySelector<HTMLInputElement>('input[aria-label="us.applicantName"]')!.value).toBe('Historical default applicant')
  expect(onChanged).toHaveBeenCalledOnce()
  expect(fetch.mock.calls.some(call => String(call[0]).endsWith('/schema?standardsSnapshotId=sha256%3Aprevious'))).toBe(true)
})
