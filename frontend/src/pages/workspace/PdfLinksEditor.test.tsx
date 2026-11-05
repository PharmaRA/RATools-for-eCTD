import { act, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it } from 'vitest'
import { PdfLinksEditor, type LinkEdit, type NavigationLink } from './PdfLinksEditor'

const disposers: Array<() => void> = []
afterEach(() => disposers.splice(0).forEach(dispose => dispose()))
const original: NavigationLink[] = [
  { objectPath: 'page[1]/annotation[1]', sourcePageNumber: 1, kind: 1, target: 'overview', namedDestination: 'overview', targetPageNumber: 1, fileSpecification: null, isValid: true },
  { objectPath: 'page[1]/annotation[2]', sourcePageNumber: 1, kind: 0, target: '2', namedDestination: null, targetPageNumber: 2, fileSpecification: null, isValid: true },
]
function render(unknownDestinations = false) {
  let reviewed: readonly LinkEdit[] | undefined
  let replaceOriginal!: (links: NavigationLink[]) => void
  function Host() {
    const [links, setLinks] = useState(original)
    useEffect(() => { replaceOriginal = setLinks }, [])
    const [value, setValue] = useState<readonly LinkEdit[]>([])
    const [valid, setValid] = useState(true)
    return <><PdfLinksEditor original={links} value={value} pageCount={2} namedDestinations={['overview']}
      remoteTargets={[{ id: 'history', label: '历史 0000：研究报告', fileSpecification: '../../0000/m5/report.pdf',
        placementId: 'prior-placement', versionId: 'prior-version', namedDestinations: unknownDestinations ? null : ['results'] }]}
      onChange={setValue} onValidityChange={setValid} />
      <button disabled={!valid} onClick={() => { reviewed = value }}>生成预览</button></>
  }
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container); act(() => root.render(<Host />))
  disposers.push(() => { act(() => root.unmount()); container.remove() })
  return Object.assign(() => reviewed, { refresh: (links: NavigationLink[]) => act(() => replaceOriginal(links)) })
}
function button(name: string) {
  const found = Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)
  if (!found) throw new Error(`Button ${name} not found`)
  return found
}
const click = (name: string) => act(() => button(name).click())
const select = (label: string, value: string) => act(() => {
  const input = document.querySelector<HTMLSelectElement>(`select[aria-label="${label}"]`)!
  input.value = value; input.dispatchEvent(new Event('change', { bubbles: true }))
})
const edit = (label: string, value: string) => act(() => {
  const input = document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})

it('keeps reserved-looking destination names distinct from remote page instructions', () => {
  const reviewed = render(true); click('修改链接 1'); select('链接 1 目标类型', '2'); select('链接 1 目标文件', 'history')
  select('链接 1 远程目标模式', 'named'); edit('链接 1 远程命名目的地', 'page=2'); click('生成预览')
  expect(reviewed()?.[0]?.target).toBe('../../0000/m5/report.pdf#nameddest=page=2')
})

it('changes only the selected annotation and binds its inspected original target', () => {
  const reviewed = render(); click('修改链接 2'); edit('链接 2 页码', '1'); click('生成预览')
  expect(reviewed()).toEqual([{ sourcePageNumber: 1, objectPath: 'page[1]/annotation[2]', originalTarget: '2',
    targetKind: 0, target: '1', targetPlacementId: null, targetVersionId: null }])
  click('保留链接 2'); click('生成预览'); expect(reviewed()).toEqual([])
})

it('requires explicit remote selection and sends the chosen historical identity and destination', () => {
  const reviewed = render(); click('修改链接 1'); select('链接 1 目标类型', '2')
  expect(button('生成预览').disabled).toBe(true)
  select('链接 1 目标文件', 'history'); select('链接 1 远程目标模式', 'named'); select('链接 1 远程目的地', 'results'); click('生成预览')
  expect(reviewed()).toEqual([{ sourcePageNumber: 1, objectPath: 'page[1]/annotation[1]', originalTarget: 'overview',
    targetKind: 2, target: '../../0000/m5/report.pdf#nameddest=results', targetPlacementId: 'prior-placement', targetVersionId: 'prior-version' }])
  select('链接 1 目标类型', '0'); click('生成预览')
  expect(reviewed()?.[0]).toMatchObject({ targetKind: 0, target: '1', targetPlacementId: null, targetVersionId: null })
})

it('constructs an explicit page fragment for a selected remote target', () => {
  const reviewed = render(); click('修改链接 1'); select('链接 1 目标类型', '2'); select('链接 1 目标文件', 'history')
  select('链接 1 远程目标模式', 'page'); edit('链接 1 远程页码', '12'); click('生成预览')
  expect(reviewed()?.[0]).toMatchObject({ target: '../../0000/m5/report.pdf#page=12', targetPlacementId: 'prior-placement', targetVersionId: 'prior-version' })
})

it('blocks out-of-range internal pages and requires an existing named destination', () => {
  render(); click('修改链接 2'); edit('链接 2 页码', '3')
  expect(button('生成预览').disabled).toBe(true)
  select('链接 2 目标类型', '1')
  expect(button('生成预览').disabled).toBe(true)
  select('链接 2 命名目的地', 'overview')
  expect(button('生成预览').disabled).toBe(false)
})

it('rejects script URLs and accepts an explicit https URI', () => {
  const reviewed = render(); click('修改链接 1'); select('链接 1 目标类型', '3')
  edit('链接 1 网址', 'javascript:alert(1)')
  expect(button('生成预览').disabled).toBe(true)
  edit('链接 1 网址', 'https://example.org/reference'); click('生成预览')
  expect(reviewed()?.[0]).toMatchObject({ targetKind: 3, target: 'https://example.org/reference' })
})

it('invalidates an edit when the inspected target changes at the same annotation', () => {
  const reviewed = render(); click('修改链接 1')
  reviewed.refresh([{ ...original[0], target: 'appendix', namedDestination: 'appendix' }, original[1]])
  expect(button('生成预览').disabled).toBe(true)
  expect(document.body.textContent).toContain('原链接已变化')
  click('保留链接 1'); click('生成预览'); expect(reviewed()).toEqual([])
})

it('shows a removed annotation as a stale edit and allows explicit withdrawal', () => {
  const reviewed = render(); click('修改链接 2'); reviewed.refresh([original[0]])
  expect(button('生成预览').disabled).toBe(true)
  expect(document.body.textContent).toContain('原链接已变化')
  click('撤销失效链接修改'); click('生成预览'); expect(reviewed()).toEqual([])
})

it('accepts an explicit remote destination for server plan verification when target facts are not loaded', () => {
  const reviewed = render(true); click('修改链接 1'); select('链接 1 目标类型', '2'); select('链接 1 目标文件', 'history')
  select('链接 1 远程目标模式', 'named'); edit('链接 1 远程命名目的地', '结论')
  click('生成预览')
  expect(reviewed()?.[0]).toMatchObject({ target: '../../0000/m5/report.pdf#nameddest=结论', targetPlacementId: 'prior-placement' })
  expect(document.body.textContent).toContain('生成计划时校验')
})
