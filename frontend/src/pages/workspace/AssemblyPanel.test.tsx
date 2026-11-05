import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { AssemblyPanel } from './AssemblyPanel'

const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.unstubAllGlobals(); localStorage.clear() })
const row = { lineNumber: 2, documentId: 'doc', nodeInstanceId: 'node', title: null, operation: 'new',
  targetLeafAddress: null, sortOrder: 0, status: 0, diagnostics: [], sourceFile: 'document:doc', fileName: 'report.pdf',
  sourceSha256: 'c'.repeat(64), sourceLength: 100, targetHref: 'm3/alpha/report.pdf' }
const previewPlan = { id: 'preview', createdUtc: '', expiresUtc: '', manifestSha256: '2ba78aa465cb332fc300e245dcb54022f5db71ab5cc1951c3a150f40abe76e84', format: 'csv',
  selectedRows: [2], canExecute: true, sources: [], plan: { applicationId: 'app', sequenceNumber: '0001', expectedRevision: 3,
    planDigest: 'a'.repeat(64), csvVersion: 'assembly-manifest-v1', totalRows: 1, validRows: 1, invalidRows: 0, diagnostics: [], rows: [row] } }
const completed = { id: 'execution', planId: 'preview', status: 2, stage: 'completed', attemptCount: 1,
  cancellationRequested: false, failureCode: null, failureMessage: null, result: { jobId: 'execution', planId: 'preview',
    planDigest: previewPlan.plan.planDigest, applicationId: 'app', sequenceNumber: '0001', expectedRevision: 3, workspaceRevision: 4,
    completedUtc: '', documents: [{ lineNumber: 2, placementId: 'placement', documentId: 'new-document', versionId: 'version',
      href: 'm3/alpha/report.pdf', leafId: 'new-leaf', historicalTargetId: null }] } }
const json = (value: unknown) => new Response(JSON.stringify(value))
const render = (onExecuted = vi.fn()) => {
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  const update = (revision = 3, disabled = false, appId = 'app') => act(() => root.render(<AssemblyPanel appId={appId} seqNumber="0001"
    workspaceRevision={revision} disabled={disabled} onExecuted={onExecuted} />))
  update(); disposers.push(() => { act(() => root.unmount()); container.remove() }); return update
}
const button = (name: string) => {
  const value = Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)
  if (!value) throw new Error(`Button ${name} not found`)
  return value
}
const click = (name: string) => act(async () => { button(name).click(); await Promise.resolve() })
const edit = (value: string) => act(() => {
  const input = document.querySelector('textarea')!
  Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})
const check = (label: string) => act(() => document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!.click())
async function prepare() {
  edit('manifest A'); await click('生成装配预览')
  for (let attempt = 0; attempt < 100 && button('生成装配预览').disabled; attempt++) {
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
  }
  expect(button('生成装配预览').disabled).toBe(false)
}

it('enqueues only the stored plan digest and not a mutable manifest', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockResolvedValueOnce(json(completed))
  const executed = vi.fn()
  vi.stubGlobal('fetch', fetch); render(executed); await prepare(); await click('执行已预览计划')
  expect(fetch.mock.calls[1][0]).toContain('/applications/app/sequences/0001/assembly-executions')
  const request = JSON.parse(fetch.mock.calls[1][1].body)
  expect(request).toEqual({ planId: 'preview', planDigest: previewPlan.plan.planDigest, idempotencyKey: expect.any(String) })
  expect(executed).toHaveBeenCalledOnce()
  expect(document.body.textContent).toContain('工作区修订已更新为 4')
})

it('creates a new immutable selected subset while still displaying invalid rows', async () => {
  const invalid = { ...row, lineNumber: 3, status: 1, diagnostics: [{ code: 'INVALID', message: 'Missing source', lineNumber: 3 }] }
  const mixed = { ...previewPlan, canExecute: false, selectedRows: [], plan: { ...previewPlan.plan,
    totalRows: 2, invalidRows: 1, rows: [row, invalid] } }
  const selected = { ...mixed, id: 'subset', canExecute: true, selectedRows: [2], plan: { ...mixed.plan, planDigest: 'b'.repeat(64) } }
  const fetch = vi.fn().mockResolvedValueOnce(json(mixed)).mockResolvedValueOnce(json(selected))
  vi.stubGlobal('fetch', fetch); render(); await prepare()
  expect(button('执行已预览计划').disabled).toBe(true)
  expect(document.body.textContent).toContain('Missing source')
  expect(document.querySelector<HTMLInputElement>('input[aria-label="选择装配行 3"]')!.disabled).toBe(true)
  check('选择装配行 2'); await click('预览所选有效行')
  expect(fetch.mock.calls[1][0]).toContain('/assembly-plans/preview/selection')
  expect(JSON.parse(fetch.mock.calls[1][1].body).selectedRows).toEqual([2])
  expect(button('执行已预览计划').disabled).toBe(false)
  expect(document.body.textContent).toContain('Missing source')
})

it('withdraws the reviewed preview when the manifest is edited', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(previewPlan)))
  render(); await prepare(); expect(button('执行已预览计划').disabled).toBe(false)
  edit('manifest B'); expect(button('执行已预览计划').disabled).toBe(true)
})

it('ignores a preview that completes after its input changes', async () => {
  let finish!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn().mockReturnValueOnce(new Promise<Response>(resolve => { finish = resolve })))
  render(); edit('manifest A'); await click('生成装配预览'); edit('manifest B')
  await act(async () => finish(json(previewPlan)))
  expect(button('执行已预览计划').disabled).toBe(true)
})

it('recovers a lost enqueue response by plan without creating another execution', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(json(completed))
  const executed = vi.fn()
  vi.stubGlobal('fetch', fetch); const update = render(executed); await prepare(); await click('执行已预览计划')
  update(4); await click('恢复装配任务')
  expect(fetch.mock.calls[2][0]).toContain('/assembly-plans/preview/execution')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(executed).toHaveBeenCalledOnce()
})

it('uses the same request identity to recover enqueue after the server proves no job exists', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 })).mockResolvedValueOnce(json(completed))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览计划')
  const request = JSON.parse(fetch.mock.calls[1][1].body)
  update(); await click('恢复装配任务')
  expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual(request)
})

it('allows read-only status recovery without re-enqueueing a missing execution', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 }))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览计划')
  update(3, true); await click('恢复装配任务')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(document.body.textContent).toContain('只读')
})

it('cancels an executing job without losing the original request handle', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockResolvedValueOnce(json({ ...completed, status: 1, result: null }))
    .mockResolvedValueOnce(json({ ...completed, status: 4, result: null }))
  vi.stubGlobal('fetch', fetch); render(); await prepare(); await click('执行已预览计划'); await click('取消装配任务')
  expect(fetch.mock.calls[2][0]).toContain('/assembly-executions/execution/cancel')
  expect(document.body.textContent).toContain('已取消')
  expect(localStorage.getItem('ratools.assembly:["app","0001"]')).toContain('preview')
})

it('does not erase a newer execution handle from another tab', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(previewPlan)).mockResolvedValueOnce(json(completed)))
  render(); await prepare(); await click('执行已预览计划')
  const storageKey = 'ratools.assembly:["app","0001"]'
  const newer = { planId: 'new-plan', planDigest: 'e'.repeat(64), idempotencyKey: 'new-key', expectedRevision: 4 }
  localStorage.setItem(storageKey, JSON.stringify(newer))
  await click('创建新装配计划')
  expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual(newer)
  expect(document.body.textContent).toContain('较新的装配任务')
})

const upload = async (file: File) => act(async () => {
  const input = document.querySelector<HTMLInputElement>('input[aria-label="导入装配清单"]')!
  Object.defineProperty(input, 'files', { configurable: true, value: [file] })
  input.dispatchEvent(new Event('change', { bubbles: true })); await Promise.resolve()
})

it('submits XLSX as its exact byte representation and never parses formulas in the browser', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json({ ...previewPlan, format: 'xlsx', manifestSha256: '9eff08db19db8dd4c8acc9440513e1dde21f6ee634489235a3cae6daf3a6c360' }))
  vi.stubGlobal('fetch', fetch); render()
  const file = new File(['unused'], 'plan.xlsx')
  Object.defineProperty(file, 'arrayBuffer', { value: async () => new Uint8Array([0x50, 0x4b, 0, 1, 255]).buffer })
  await upload(file); await click('生成装配预览')
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ expectedRevision: 3, manifest: '', format: 'xlsx', manifestBase64: 'UEsAAf8=' })
  expect(document.body.textContent).toContain('公式不执行')
})

it('rejects oversized files before reading or submitting them', async () => {
  const fetch = vi.fn(), read = vi.fn()
  vi.stubGlobal('fetch', fetch); render()
  const file = new File(['x'], 'oversized.xlsx')
  Object.defineProperty(file, 'size', { value: 2 * 1024 * 1024 + 1 })
  Object.defineProperty(file, 'arrayBuffer', { value: read })
  await upload(file)
  expect(read).not.toHaveBeenCalled(); expect(fetch).not.toHaveBeenCalled()
  expect(document.body.textContent).toContain('不得超过 2 MiB')
})

it('exports diagnosis cells as quoted text with formula prefixes neutralized', async () => {
  const create = vi.fn().mockReturnValue('blob:diagnostics'), revoke = vi.fn()
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: revoke })
  const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  disposers.push(() => clicked.mockRestore())
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json({ ...previewPlan, plan: { ...previewPlan.plan,
    diagnostics: [{ code: 'EXTERNAL', message: '=HYPERLINK("external")' }] } })))
  render(); await prepare(); await click('导出问题清单')
  const blob = create.mock.calls[0][0] as Blob
  const content = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsText(blob)
  })
  expect(content).toContain('"\'=HYPERLINK(""external"")"')
  expect(content).toContain('lineNumber')
})

it('does not enqueue if recovery becomes read-only while the lookup is pending', async () => {
  let finish!: (response: Response) => void
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockRejectedValueOnce(new TypeError('lost response'))
    .mockReturnValueOnce(new Promise<Response>(resolve => { finish = resolve }))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览计划')
  await click('恢复装配任务'); update(3, true)
  await act(async () => finish(new Response('', { status: 404 })))
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(document.body.textContent).toContain('只读')
})

it.each([
  ['application', { applicationId: 'other-app' }],
  ['digest', { planDigest: 'e'.repeat(64) }],
  ['revision', { workspaceRevision: 999 }],
  ['job', { jobId: 'other-job' }],
])('rejects completed audit evidence for another %s', async (_reason, result) => {
  const executed = vi.fn()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(previewPlan))
    .mockResolvedValueOnce(json({ ...completed, result: { ...completed.result, ...result } })))
  render(executed); await prepare(); await click('执行已预览计划')
  expect(executed).not.toHaveBeenCalled()
  expect(document.body.textContent).toContain('装配完成证据与当前请求不一致')
})

it.each([
  ['input hash', { manifestSha256: 'f'.repeat(64) }],
  ['format', { format: 'xlsx' }],
])('rejects previews with a different %s', async (_reason, response) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json({ ...previewPlan, ...response })))
  render(); await prepare()
  expect(button('执行已预览计划').disabled).toBe(true)
  expect(document.body.textContent).toContain('装配预览与本次清单内容不一致')
})

it('returns to editable input when no job exists and recovery definitively rejects an expired plan', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(previewPlan)).mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(new Response('', { status: 404 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'Plan expired' }), { status: 409 }))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览计划')
  update(4); await click('恢复装配任务')
  expect(localStorage.getItem('ratools.assembly:["app","0001"]')).toBe('null')
  edit('new manifest')
  expect(button('生成装配预览').disabled).toBe(false)
})
