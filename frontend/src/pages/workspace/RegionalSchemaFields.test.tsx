import { act, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it } from 'vitest'
import type { RegionalFieldDescriptor } from '../../api/generated'
import { RegionalSchemaFields } from './RegionalSchemaFields'
import { withRegionalValue } from './regionalMetadataValues'

const disposers: Array<() => void> = []
afterEach(() => disposers.splice(0).forEach(dispose => dispose()))
const field = (key: string, kind = 'text', extra: Partial<RegionalFieldDescriptor> = {}): RegionalFieldDescriptor =>
  ({ key, label: key, kind, required: false, fields: [], options: [], conditions: [], identityField: null, source: null, businessEvidence: null, ...extra })
let latest: Record<string, unknown>
function render(fields: RegionalFieldDescriptor[], initial: Record<string, unknown>, disabled = false) {
  function Harness() {
    const [value, setValue] = useState(initial)
    useEffect(() => { latest = value }, [value])
    return <RegionalSchemaFields fields={fields} value={value} disabled={disabled}
      nodes={[{ id: 'node-a', label: 'Node A', ctdSection: 'm1' }, { id: 'node-pi', label: 'PI Node', ctdSection: 'm1.3.1' }]}
      placements={[{ id: 'placement-a', label: 'A.pdf', nodeInstanceId: 'node-a' }]}
      onChange={(path, next) => setValue(current => withRegionalValue(current, path, next))} />
  }
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container); act(() => root.render(<Harness />))
  disposers.push(() => { act(() => root.unmount()); container.remove() })
}
const edit = (path: string, value: string) => act(() => {
  const input = document.querySelector<HTMLInputElement>(`input[aria-label="${path}"]`)!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})
const click = (label: string) => act(() => {
  const item = Array.from(document.querySelectorAll('button')).find(button => button.getAttribute('aria-label') === label)
  if (!item) throw new Error('Missing button ' + label)
  item.click()
})

it('takes controlled values from the supplied schema while retaining an unsupported entered value', () => {
  render([field('country', 'enum', { options: ['de', 'fr'] })], { country: 'legacy-code' })
  expect(document.querySelector<HTMLInputElement>('input[aria-label="country"]')!.value).toBe('legacy-code')
  expect(Array.from(document.querySelectorAll('datalist option')).map(item => item.getAttribute('value'))).toEqual(['de', 'fr'])
  edit('country', 'fr'); expect(latest.country).toBe('fr')
})

it('keeps a conditionally hidden value when the controlling procedure changes', () => {
  render([field('procedure'), field('submissionMode', 'text', { conditions: [{ fieldPath: 'procedure', values: ['centralised'], description: 'Centralised only', effect: 'visible' }] })],
    { procedure: 'centralised', submissionMode: 'grouping' })
  edit('procedure', 'national')
  expect(latest.submissionMode).toBe('grouping')
  expect(document.body.textContent).toContain('grouping')
  expect(document.body.textContent).toContain('已保留')
})

it('reorders equal-valued rows by stable identity without changing a neighbouring entry', () => {
  render([field('names', 'collection', { identityField: 'id', fields: [field('value')] })],
    { names: [{ id: 'first', value: 'Same' }, { id: 'second', value: 'Same' }] })
  click('上移 names[1]'); edit('names[0].value', 'Changed second')
  expect(latest.names).toEqual([{ id: 'second', value: 'Changed second' }, { id: 'first', value: 'Same' }])
})

it('binds a document context to a chosen real placement and its actual node without filling business values', () => {
  render([field('documentContexts', 'collection', { identityField: 'placementId', fields: [field('country'), field('language')] })], { documentContexts: [] })
  click('新增 documentContexts')
  const selector = document.querySelector<HTMLSelectElement>('select[aria-label="documentContexts[0].placementId"]')!
  act(() => { selector.value = 'placement-a'; selector.dispatchEvent(new Event('change', { bubbles: true })) })
  expect(latest.documentContexts).toEqual([{ placementId: 'placement-a', nodeInstanceId: 'node-a', country: null, language: null }])
  expect(Array.from(selector.options).map(item => item.value)).toEqual(['', 'placement-a'])
})

it('makes finalized fields and collection operations read-only', () => {
  render([field('country'), field('names', 'collection', { identityField: 'id', fields: [field('value')] })], { country: 'de', names: [] }, true)
  expect(document.querySelector<HTMLInputElement>('input[aria-label="country"]')!.disabled).toBe(true)
  expect(document.querySelector<HTMLButtonElement>('button[aria-label="新增 names"]')!.disabled).toBe(true)
})

it('keeps related-sequence input visible for response when an initial-unit condition is guidance', () => {
  render([field('submissionUnit'), field('relatedSequences', 'collection', { identityField: 'id', fields: [field('sequenceNumber')],
    conditions: [{ fieldPath: 'submissionUnit', values: ['initial', 'reformat'], description: 'Initial uses current; response uses earlier', effect: 'guidance' }] })],
    { submissionUnit: 'response', relatedSequences: [{ id: 'related-a', sequenceNumber: '0000' }] })
  expect(document.querySelector<HTMLInputElement>('input[aria-label="relatedSequences[0].sequenceNumber"]')!.value).toBe('0000')
})

it('uses the selected actual node section for conditional requirements without serializing section into metadata', () => {
  const condition = { fieldPath: 'ctdSection', values: ['m1.3.1'], description: 'PI node', effect: 'required' }
  render([field('nodeContexts', 'collection', { identityField: 'nodeInstanceId', fields: [field('country', 'text', { conditions: [condition] }),
    field('productInformationType', 'text', { conditions: [{ ...condition, effect: 'visible' }, condition] })] })],
  { nodeContexts: [{ nodeInstanceId: 'node-a', country: 'de', productInformationType: 'spc' }] })
  const country = document.querySelector<HTMLInputElement>('input[aria-label="nodeContexts[0].country"]')!
  expect(country.getAttribute('aria-required')).toBe('false')
  expect(document.querySelector('input[aria-label="nodeContexts[0].productInformationType"]')).toBeNull()
  const selector = document.querySelector<HTMLSelectElement>('select[aria-label="nodeContexts[0].nodeInstanceId"]')!
  act(() => { selector.value = 'node-pi'; selector.dispatchEvent(new Event('change', { bubbles: true })) })
  expect(document.querySelector<HTMLInputElement>('input[aria-label="nodeContexts[0].country"]')!.getAttribute('aria-required')).toBe('true')
  expect(document.querySelector<HTMLInputElement>('input[aria-label="nodeContexts[0].productInformationType"]')!.value).toBe('spc')
  expect(latest.nodeContexts).toEqual([{ nodeInstanceId: 'node-pi', country: 'de', productInformationType: 'spc' }])
})
