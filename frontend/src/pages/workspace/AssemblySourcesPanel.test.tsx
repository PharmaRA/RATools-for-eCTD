import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { AssemblySourcesPanel } from './AssemblySourcesPanel'

const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.unstubAllGlobals() })
const first = { documentId: 'doc-a', sourceFile: 'document:doc-a', fileName: 'same.pdf', expectedRevision: 3,
  length: 100, sha256: 'a'.repeat(64), md5: 'b'.repeat(32) }
const second = { ...first, documentId: 'doc-b', sourceFile: 'document:doc-b' }
function render(disabled = false) {
  const onManifest = vi.fn(), container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  act(() => root.render(<AssemblySourcesPanel appId="app" seqNumber="0001" expectedRevision={3} disabled={disabled}
    nodes={[{ id: 'alpha', label: 'Drug A · Alpha' }, { id: 'beta', label: 'Drug A · Beta' }]} onManifest={onManifest} />))
  disposers.push(() => { act(() => root.unmount()); container.remove() })
  return onManifest
}
const button = (label: string) => {
  const value = Array.from(document.querySelectorAll('button')).find(item => item.textContent === label)
  if (!value) throw new Error('Missing button ' + label)
  return value
}
const click = (name: string) => act(async () => { button(name).click(); await Promise.resolve() })
const node = (id: string, selected: string) => act(() => {
  const value = document.querySelector<HTMLSelectElement>(`select[aria-label="来源目标节点 ${id}"]`)!
  value.value = selected; value.dispatchEvent(new Event('change', { bubbles: true }))
})
const json = (value: unknown) => new Response(JSON.stringify(value))
const sourcePage = (items: typeof first[], offset = 0, total = items.length) => ({ items, offset, limit: 1000, total })

it('recovers more than 1000 cumulative sources in bounded pages while limiting each manifest selection', async () => {
  const items = Array.from({ length: 1000 }, (_, index) => ({ ...first, documentId: `doc-${index}`, sourceFile: `document:doc-${index}` }))
  const last = { ...first, documentId: 'doc-1000', sourceFile: 'document:doc-1000' }
  const fetch = vi.fn().mockResolvedValueOnce(json(sourcePage(items, 0, 1001)))
    .mockResolvedValueOnce(json(sourcePage([last], 1000, 1001)))
  vi.stubGlobal('fetch', fetch); render()
  await click('读取已上传装配来源')
  await click('加载更多装配来源')
  expect(fetch.mock.calls[0][0]).toContain('?offset=0&limit=1000')
  expect(fetch.mock.calls[1][0]).toContain('?offset=1000&limit=1000')
  expect(document.body.textContent).toContain('已读取 1001 个来源，选择 1000 个')
  expect(button('加载更多装配来源').disabled).toBe(true)
  expect(document.querySelector('[role="alert"]')).toBeNull()
})

it('requires explicit node mapping and distinguishes same-name uploaded sources', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(sourcePage([first, second]))))
  const onManifest = render(); await click('读取已上传装配来源')
  expect(button('将所选来源填入清单').disabled).toBe(true)
  node('doc-a', 'alpha'); node('doc-b', 'beta')
  await click('将所选来源填入清单')
  const csv = onManifest.mock.lastCall![0] as string
  expect(csv).toContain('"document:doc-a","alpha"')
  expect(csv).toContain('"document:doc-b","beta"')
  expect(csv).toContain('"same.pdf"')
})

it('records each successful upload and reports failures without creating mounts', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(first))
    .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'File refused' }), { status: 400 }))
  vi.stubGlobal('fetch', fetch); render()
  const input = document.querySelector<HTMLInputElement>('input[aria-label="上传装配源文件"]')!
  Object.defineProperty(input, 'files', { value: [new File(['pdf'], 'a.pdf'), new File(['bad'], 'b.pdf')] })
  await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); await Promise.resolve() })
  expect(fetch).toHaveBeenCalledTimes(2)
  expect(fetch.mock.calls.every(call => String(call[0]).endsWith('/assembly-sources'))).toBe(true)
  expect((fetch.mock.calls[0][1].body as FormData).get('expectedRevision')).toBe('3')
  expect(document.body.textContent).toContain('document:doc-a')
  expect(document.body.textContent).toContain('File refused')
  expect(button('将所选来源填入清单').disabled).toBe(true)
})

it('recovers accepted source identities after upload response loss', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new TypeError('response lost')).mockResolvedValueOnce(json(sourcePage([first])))
  vi.stubGlobal('fetch', fetch); render()
  const input = document.querySelector<HTMLInputElement>('input[aria-label="上传装配源文件"]')!
  Object.defineProperty(input, 'files', { value: [new File(['pdf'], 'a.pdf')] })
  await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); await Promise.resolve() })
  expect(document.body.textContent).toContain('response lost')
  await click('读取已上传装配来源')
  expect(document.body.textContent).toContain('document:doc-a')
})

it('permits read-only source discovery while keeping upload and manifest writes disabled', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(sourcePage([first]))))
  const onManifest = render(true); await click('读取已上传装配来源')
  expect(document.querySelector<HTMLInputElement>('input[aria-label="上传装配源文件"]')!.disabled).toBe(true)
  expect(button('将所选来源填入清单').disabled).toBe(true)
  expect(onManifest).not.toHaveBeenCalled()
})

it('clears the file selection so a failed file can be chosen again for retry', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new TypeError('connection failed')).mockResolvedValueOnce(json(first))
  vi.stubGlobal('fetch', fetch); render()
  const input = document.querySelector<HTMLInputElement>('input[aria-label="上传装配源文件"]')!
  const file = new File(['pdf'], 'retry.pdf')
  Object.defineProperty(input, 'files', { configurable: true, value: [file] })
  Object.defineProperty(input, 'value', { configurable: true, writable: true, value: 'C:\\fakepath\\retry.pdf' })
  await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); await Promise.resolve() })
  expect(input.value).toBe('')
  input.value = 'C:\\fakepath\\retry.pdf'
  await act(async () => { input.dispatchEvent(new Event('change', { bubbles: true })); await Promise.resolve() })
  expect(fetch).toHaveBeenCalledTimes(2)
  expect(document.body.textContent).toContain('document:doc-a')
})
