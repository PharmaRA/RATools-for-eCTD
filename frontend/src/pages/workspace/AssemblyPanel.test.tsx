import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { ConfigProvider } from 'antd'
import { afterEach, expect, it, vi } from 'vitest'
import { AssemblyPanel } from './AssemblyPanel'
import type { AssemblyPlan } from '../../api/generated'

const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.unstubAllGlobals() })
const plan: AssemblyPlan = {
  applicationId: 'app', sequenceNumber: '0001', expectedRevision: 3, planDigest: 'a'.repeat(64),
  csvVersion: 'assembly-manifest-v1', totalRows: 1, validRows: 1, invalidRows: 0, diagnostics: [],
  rows: [{ lineNumber: 2, documentId: 'doc', nodeInstanceId: 'node', title: null, operation: 'new',
    targetLeafAddress: null, sortOrder: 0, status: 0, diagnostics: [] }],
}
const render = () => {
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  const update = (revision = 3, disabled = false, appId = 'app') => act(() => root.render(
    <ConfigProvider button={{ autoInsertSpace: false }}><AssemblyPanel appId={appId} seqNumber="0001"
      workspaceRevision={revision} disabled={disabled} onExecuted={() => {}} /></ConfigProvider>))
  update()
  disposers.push(() => { act(() => root.unmount()); container.remove() })
  return update
}
const waitFor = async (predicate: () => boolean) => {
  for (let n = 0; n < 100; n++) { await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) }); if (predicate()) return }
  throw new Error('Expected assembly state did not appear')
}
const button = (name: string) => Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)!
const edit = (value: string) => act(() => {
  const input = document.querySelector('textarea')!
  Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})
const preview = () => act(() => button('生成装配预览').click())

it('renders numeric valid status and executes the exact reviewed input', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(plan)))
    .mockResolvedValueOnce(new Response(JSON.stringify({ ...plan, workspaceRevision: 4, placementIds: ['placement'] })))
  vi.stubGlobal('fetch', fetch); render(); edit('manifest A'); preview()
  await waitFor(() => document.querySelector('tbody')?.textContent?.includes('doc') === true)
  expect(document.querySelector('tbody')!.textContent).toContain('有效')
  expect(document.querySelector('tbody')!.textContent).not.toContain('无效')
  await act(async () => button('执行已预览计划').click())
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toMatchObject({ manifest: 'manifest A', expectedRevision: 3, planDigest: plan.planDigest })
})

it('withdraws a reviewed plan when its CSV is edited', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(plan))))
  render(); edit('manifest A'); preview()
  await waitFor(() => !button('执行已预览计划').disabled)
  edit('manifest B')
  expect(button('执行已预览计划').disabled).toBe(true)
  expect(document.querySelector('tbody')).toBeNull()
})

it('ignores a preview that completes after the CSV changes', async () => {
  let complete!: (value: Response) => void
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { complete = resolve })))
  render(); edit('manifest A'); preview(); edit('manifest B')
  await act(async () => { complete(new Response(JSON.stringify(plan))) })
  expect(button('执行已预览计划').disabled).toBe(true)
  expect(document.querySelector('tbody')).toBeNull()
})

it.each(['revision', 'disabled', 'application'])('invalidates execution when %s changes', async kind => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(plan))))
  const update = render(); edit('manifest A'); preview()
  await waitFor(() => !button('执行已预览计划').disabled)
  update(kind === 'revision' ? 4 : 3, kind === 'disabled', kind === 'application' ? 'other-app' : 'app')
  expect(button('执行已预览计划').disabled).toBe(true)
})

it('does not enable execution for a plan with no executable rows', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...plan, rows: [], totalRows: 0, validRows: 0 }))))
  render(); edit('header only'); preview()
  await waitFor(() => document.body.textContent!.includes('总行数 0'))
  expect(button('执行已预览计划').disabled).toBe(true)
})
