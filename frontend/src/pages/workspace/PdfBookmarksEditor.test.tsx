import { act, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it } from 'vitest'
import { PdfBookmarksEditor, type BookmarkEdit } from './PdfBookmarksEditor'

const disposers: Array<() => void> = []
afterEach(() => disposers.splice(0).forEach(dispose => dispose()))

const original = [
  { title: '章节一', level: 0, pageNumber: 1, namedDestination: null },
  { title: '中文子项', level: 1, pageNumber: null, namedDestination: 'overview' },
] satisfies BookmarkEdit[]

function render() {
  let reviewed: readonly BookmarkEdit[] | null | undefined
  function Host() {
    const [value, setValue] = useState<readonly BookmarkEdit[] | null>(null)
    const [valid, setValid] = useState(true)
    return <><PdfBookmarksEditor original={original} pageCount={2} namedDestinations={['overview']}
      value={value} onChange={setValue} onValidityChange={setValid} />
      <button disabled={!valid} onClick={() => { reviewed = value }}>生成预览</button></>
  }
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  act(() => root.render(<Host />))
  disposers.push(() => { act(() => root.unmount()); container.remove() })
  return () => reviewed
}

const button = (name: string) => {
  const result = Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)
  if (!result) throw new Error(`Button ${name} not found`)
  return result
}
const click = (name: string) => act(() => button(name).click())
const edit = (label: string, value: string) => act(() => {
  const input = document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})

it('preserves original bookmarks until an edit and explicitly clears them when requested', () => {
  const reviewed = render()
  expect(document.body.textContent).toContain('章节一')
  click('生成预览'); expect(reviewed()).toBeNull()
  click('编辑书签'); click('清空全部书签'); click('生成预览')
  expect(reviewed()).toEqual([])
  click('保留原书签'); click('生成预览'); expect(reviewed()).toBeNull()
})

it('retains Unicode hierarchy and named destinations while editing one title', () => {
  const reviewed = render(); click('编辑书签')
  edit('书签 1 标题', '修订后的章节'); click('生成预览')
  expect(reviewed()).toEqual([
    { title: '修订后的章节', level: 0, pageNumber: 1, namedDestination: null },
    { title: '中文子项', level: 1, pageNumber: null, namedDestination: 'overview' },
  ])
})

it('blocks out-of-range pages and skipped hierarchy levels until corrected', () => {
  render(); click('编辑书签')
  edit('书签 1 页码', '3')
  expect(button('生成预览').disabled).toBe(true)
  expect(document.body.textContent).toContain('页码必须在 1–2 之间')
  edit('书签 1 页码', '2'); edit('书签 2 层级', '3')
  expect(button('生成预览').disabled).toBe(true)
  expect(document.body.textContent).toContain('层级不能跳过父级')
  edit('书签 2 层级', '1')
  expect(button('生成预览').disabled).toBe(false)
})

it('blocks a child orphaned by removal and allows it to be promoted to a root', () => {
  const reviewed = render(); click('编辑书签'); click('移除书签 1')
  expect(button('生成预览').disabled).toBe(true)
  edit('书签 1 层级', '0'); click('生成预览')
  expect(reviewed()).toEqual([{ title: '中文子项', level: 0, pageNumber: null, namedDestination: 'overview' }])
})

it('adds a bookmark with explicit title and supports reordering roots', () => {
  const reviewed = render(); click('编辑书签'); click('添加书签')
  expect(button('生成预览').disabled).toBe(true)
  edit('书签 3 标题', '附录'); click('上移书签 3')
  edit('书签 3 层级', '0'); click('生成预览')
  expect(reviewed()?.map(item => item.title)).toEqual(['章节一', '附录', '中文子项'])
})
