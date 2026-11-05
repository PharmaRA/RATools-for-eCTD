import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { PdfRenamePanel } from './PdfRenamePanel'

const dispose: Array<() => void> = []
afterEach(() => { dispose.splice(0).forEach(action => action()); vi.unstubAllGlobals(); localStorage.clear() })
const preview = (approved: boolean) => ({ id: 'plan', engineVersion: 'qpdf-12.4.1', createdUtc: '2026-11-05T00:00:00Z',
  expiresUtc: '2026-11-06T00:00:00Z', sources: [], changes: [{ placementId: 'target', fileName: 'new.pdf', targetNodeInstanceId: null }],
  plan: { applicationId: 'app', sequenceNumber: '0001', expectedRevision: 7, changes: [{ placementId: 'target', newHref: 'm5/new.pdf' }],
    confirmedSources: approved ? ['target', 'referrer'] : ['target'], requiredSources: ['target', 'referrer'],
    impacts: [{ sourcePlacementId: 'referrer', targetPlacementId: 'target', objectPath: 'page[1]/annotation[1]/GoToR',
      sourcePageNumber: 1, oldReference: 'old.pdf', newReference: 'new.pdf', isUri: false, targetPageNumber: 1, namedDestination: null }],
    issues: approved ? [] : [{ code: 'SOURCE_NOT_CONFIRMED', placementId: 'referrer', objectPath: null, message: 'Confirm referring source.' }],
    planDigest: approved ? 'b'.repeat(64) : 'a'.repeat(64), canExecute: approved } })
const complete = { id: 'job', planId: 'plan', status: 2, stage: 'completed', attemptCount: 1, cancellationRequested: false,
  createdUtc: '', completedUtc: '', failureCode: null, failureMessage: null, adoption: null,
  results: [{ placementId: 'target', versionId: 'version', href: 'm5/new.pdf', sha256: 'c'.repeat(64), md5: 'd'.repeat(32) }] }
const json = (data: unknown) => new Response(JSON.stringify(data))
const candidates = [{ placementId: 'target', fileName: 'old.pdf', href: 'm5/old.pdf', nodeInstanceId: null },
  { placementId: 'referrer', fileName: 'referrer.pdf', href: 'm5/referrer.pdf', nodeInstanceId: null }]
function render(onAdopted = vi.fn()) {
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  const update = (revision = 7, disabled = false) => act(() => root.render(<PdfRenamePanel appId="app" seqNumber="0001"
    workspaceRevision={revision} disabled={disabled} candidates={candidates} nodes={[]} onAdopted={onAdopted} />))
  update(); dispose.push(() => { act(() => root.unmount()); container.remove() }); return update
}
const button = (name: string) => {
  const found = Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)
  if (!found) throw new Error(`Missing ${name}`)
  return found
}
const click = (name: string) => act(async () => { button(name).click(); await Promise.resolve() })
const edit = (label: string, value: string) => act(() => {
  const element = document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
})
const check = (label: string) => act(() => document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!.click())
async function planFirst() { check('选择改名 old.pdf'); edit('新文件名 old.pdf', 'new.pdf'); await click('预览改名影响') }

it('requires explicit affected-source confirmation and a new preview before executing', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(false))).mockResolvedValueOnce(json(preview(true)))
  vi.stubGlobal('fetch', fetch); render(); await planFirst()
  expect(document.body.textContent).toContain('old.pdf → new.pdf')
  expect(button('执行已确认改名').disabled).toBe(true)
  check('确认引用源 0001/m5/referrer.pdf')
  expect(button('执行已确认改名').disabled).toBe(true)
  await click('预览改名影响')
  expect(JSON.parse(fetch.mock.calls[1][1].body).confirmedSources).toEqual(['target', 'referrer'])
  expect(button('执行已确认改名').disabled).toBe(false)
})

it('invalidates the execution digest when a filename changes after preview', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(preview(true))))
  render(); await planFirst(); expect(button('执行已确认改名').disabled).toBe(false)
  edit('新文件名 old.pdf', 'different.pdf'); expect(button('执行已确认改名').disabled).toBe(true)
})

it('sends one batch adoption after completion and invalidates the workspace', async () => {
  const onAdopted = vi.fn()
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockResolvedValueOnce(json(complete))
    .mockResolvedValueOnce(json({ jobId: 'job', workspaceRevision: 8, documents: [] }))
  vi.stubGlobal('fetch', fetch); render(onAdopted); await planFirst(); await click('执行已确认改名')
  expect(onAdopted).not.toHaveBeenCalled()
  await click('采用全部改名结果')
  expect(fetch.mock.calls[2][0]).toContain('/pdf-renames/jobs/job/adopt')
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ expectedRevision: 7 })
  expect(onAdopted).toHaveBeenCalledOnce()
})

it('recovers the same request after a lost response and revision change without allowing stale adoption', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 })).mockResolvedValueOnce(json(complete))
  vi.stubGlobal('fetch', fetch); const update = render(); await planFirst(); await click('执行已确认改名')
  const request = JSON.parse(fetch.mock.calls[1][1].body)
  update(8); await click('恢复改名任务')
  expect(fetch.mock.calls[2][0]).toContain('/plans/plan/job')
  expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual(request)
  expect(button('采用全部改名结果').disabled).toBe(true)
})

it('restores an already-created job by its plan without issuing another enqueue', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(json(complete))
  vi.stubGlobal('fetch', fetch); const update = render(); await planFirst(); await click('执行已确认改名')
  update(8); await click('恢复改名任务')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(fetch.mock.calls[2][0]).toContain('/plans/plan/job')
  expect(document.body.textContent).toContain('全部结果已检查')
})

it('allows a new preview when recovery proves no job exists and the old plan is definitively rejected', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'stale plan' }), { status: 409 }))
  vi.stubGlobal('fetch', fetch); const update = render(); await planFirst(); await click('执行已确认改名')
  update(8); await click('恢复改名任务')
  check('选择改名 old.pdf')
  expect(button('预览改名影响').disabled).toBe(false)
})

it('does not discard a newer task saved by another tab when resetting an old completed task', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(preview(true))).mockResolvedValueOnce(json(complete)))
  render(); await planFirst(); await click('执行已确认改名')
  const storageKey = `ratools.pdf-renames:${JSON.stringify(['app', '0001'])}`
  const newer = { planId: 'new-plan', planDigest: 'c'.repeat(64), idempotencyKey: 'new-key', expectedRevision: 7, jobId: 'new-job' }
  localStorage.setItem(storageKey, JSON.stringify(newer))
  await click('创建新的改名计划')
  expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual(newer)
  expect(document.body.textContent).toContain('较新的改名任务')
})

it('queries saved tasks in read-only mode without enqueueing when none exists', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 }))
  vi.stubGlobal('fetch', fetch); const update = render(); await planFirst(); await click('执行已确认改名')
  update(7, true); await click('恢复改名任务')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(fetch.mock.calls[2][0]).toContain('/plans/plan/job')
  expect(document.body.textContent).toContain('只读')
})

it('does not enqueue if the workspace becomes read-only while recovery is querying', async () => {
  let finishQuery!: (response: Response) => void
  const pendingQuery = new Promise<Response>(resolve => { finishQuery = resolve })
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(true))).mockRejectedValueOnce(new TypeError('lost response'))
    .mockReturnValueOnce(pendingQuery)
  vi.stubGlobal('fetch', fetch); const update = render(); await planFirst(); await click('执行已确认改名')
  await click('恢复改名任务')
  update(7, true)
  await act(async () => { finishQuery(new Response('', { status: 404 })); await pendingQuery })
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(document.body.textContent).toContain('只读')
})

it('requires fresh referring-source confirmation when starting another batch', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(preview(false))).mockResolvedValueOnce(json(preview(true)))
    .mockResolvedValueOnce(json(complete)).mockResolvedValueOnce(json(preview(false)))
  vi.stubGlobal('fetch', fetch); render(); await planFirst(); check('确认引用源 0001/m5/referrer.pdf')
  await click('预览改名影响'); await click('执行已确认改名'); await click('创建新的改名计划'); await click('预览改名影响')
  expect(JSON.parse(fetch.mock.calls[3][1].body).confirmedSources).toEqual(['target'])
})

it('distinguishes same-name referring sources by full path and actual sequence', async () => {
  const response = preview(false)
  const fetch = vi.fn().mockResolvedValue(json({ ...response,
    sources: [
      { placementId: 'other-referrer', sequenceNumber: '0001', href: 'm4/referrer.pdf' },
      { placementId: 'historical-referrer', sequenceNumber: '0000', href: 'm5/referrer.pdf' },
    ],
    plan: { ...response.plan, requiredSources: ['target', 'referrer', 'other-referrer', 'historical-referrer'] },
  }))
  vi.stubGlobal('fetch', fetch); render(); await planFirst()
  for (const path of ['0001/m5/referrer.pdf', '0001/m4/referrer.pdf', '0000/m5/referrer.pdf']) {
    expect(document.querySelectorAll(`input[aria-label="确认引用源 ${path}"]`)).toHaveLength(1)
  }
  check('确认引用源 0001/m4/referrer.pdf')
  await click('预览改名影响')
  expect(JSON.parse(fetch.mock.calls[1][1].body).confirmedSources).toEqual(['target', 'other-referrer'])
})
