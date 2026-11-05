import { act, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { PdfContentEditor, type ContentAvailability, type ContentOptions } from './PdfContentEditor'

const disposals: Array<() => void> = []
afterEach(() => disposals.splice(0).forEach(dispose => dispose()))
const enabled = { ocr: true, fonts: true, version: true, reason: null }
function render(availability: ContentAvailability = enabled, disabled = false) {
  const onChange = vi.fn(), onValidityChange = vi.fn()
  function Harness() {
    const [value, setValue] = useState<ContentOptions | null>(null)
    return <PdfContentEditor value={value} pageCount={5} availability={availability} disabled={disabled}
      onChange={next => { onChange(next); setValue(next) }} onValidityChange={onValidityChange} />
  }
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  act(() => root.render(<Harness />))
  disposals.push(() => { act(() => root.unmount()); container.remove() })
  return { onChange, onValidityChange }
}
const toggle = (name: string) => act(() => document.querySelector<HTMLInputElement>(`input[aria-label="${name}"]`)!.click())
const editPages = (text: string) => act(() => {
  const input = document.querySelector<HTMLInputElement>('input[aria-label="OCR 页范围"]')!
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, text)
  input.dispatchEvent(new Event('input', { bubbles: true }))
})

it('starts without content changes and requires an explicit OCR choice', () => {
  const { onChange } = render()
  expect(onChange).not.toHaveBeenCalled()
  toggle('对扫描页进行 OCR')
  expect(onChange).toHaveBeenLastCalledWith({ ocr: { language: 'eng', pages: [1, 2, 3, 4, 5], dpi: 150 }, embedStandardFonts: false, targetVersion: null })
  expect(document.body.textContent).toContain('英语')
  expect(document.body.textContent).toContain('核对识别文本')
})

it('converts explicit page ranges to the selected page identities', () => {
  const { onChange, onValidityChange } = render()
  toggle('对扫描页进行 OCR'); editPages('1, 3-5')
  expect(onChange.mock.lastCall?.[0].ocr.pages).toEqual([1, 3, 4, 5])
  expect(onValidityChange).toHaveBeenLastCalledWith(true)
})

it('rejects invalid and duplicate page ranges until corrected or OCR is deselected', () => {
  const { onValidityChange } = render()
  toggle('嵌入支持的标准字体'); toggle('对扫描页进行 OCR')
  for (const invalid of ['', '0', '6', '4-2', '1,1', '2-4,3', '1e0', '1.5', '1,,2']) {
    editPages(invalid)
    expect(onValidityChange).toHaveBeenLastCalledWith(false)
    expect(document.querySelector('[role="alert"]')).not.toBeNull()
  }
  toggle('对扫描页进行 OCR')
  expect(onValidityChange).toHaveBeenLastCalledWith(true)
})

it('keeps unsupported capabilities disabled with a reason', () => {
  const { onChange } = render({ ocr: false, fonts: false, version: false, reason: '服务器尚未配置已验证内容引擎' })
  for (const label of ['对扫描页进行 OCR', '嵌入支持的标准字体', '输出为 PDF 1.7']) {
    expect(document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!.disabled).toBe(true)
    toggle(label)
  }
  expect(onChange).not.toHaveBeenCalled()
  expect(document.body.textContent).toContain('尚未配置')
})

it('sends separate font and version selections and returns null when all are cleared', () => {
  const { onChange } = render()
  toggle('嵌入支持的标准字体'); toggle('输出为 PDF 1.7')
  expect(onChange).toHaveBeenLastCalledWith({ ocr: null, embedStandardFonts: true, targetVersion: '1.7' })
  toggle('嵌入支持的标准字体'); toggle('输出为 PDF 1.7')
  expect(onChange).toHaveBeenLastCalledWith(null)
  expect(document.body.textContent).toContain('PDF/A')
})

it('does not permit editing while a reviewed task is running or the workspace is read-only', () => {
  const { onChange } = render(enabled, true)
  toggle('对扫描页进行 OCR'); toggle('嵌入支持的标准字体'); toggle('输出为 PDF 1.7')
  expect(onChange).not.toHaveBeenCalled()
})
