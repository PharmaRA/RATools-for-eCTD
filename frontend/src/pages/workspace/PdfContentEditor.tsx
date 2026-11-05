import { useEffect, useState } from 'react'

export type ContentOptions = {
  ocr: { language: 'eng'; pages: number[]; dpi: 150 } | null
  embedStandardFonts: boolean
  targetVersion: '1.7' | null
}
export type ContentAvailability = { ocr: boolean; fonts: boolean; version: boolean; reason: string | null }
type Props = { value: ContentOptions | null; pageCount: number; availability: ContentAvailability; disabled: boolean;
  onChange: (value: ContentOptions | null) => void; onValidityChange: (valid: boolean) => void }

const empty: ContentOptions = { ocr: null, embedStandardFonts: false, targetVersion: null }
function pagesFrom(text: string, count: number): { pages: number[]; error?: string } {
  if (!Number.isSafeInteger(count) || count < 1 || count > 500) return { pages: [], error: '当前页数超出已验证的 1–500 页范围。' }
  const pages: number[] = []
  for (const part of text.split(',')) {
    const match = /^(\d+)(?:\s*-\s*(\d+))?$/.exec(part.trim())
    if (!match) return { pages: [], error: '请填写明确页码或范围，例如 1, 3-5。' }
    const start = Number(match[1]), end = Number(match[2] ?? match[1])
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 1 || end < start || end > count)
      return { pages: [], error: `页码必须在 1–${count} 内，范围起点不能大于终点。` }
    for (let page = start; page <= end; page++) {
      if (pages.includes(page)) return { pages: [], error: '页码或范围重复，请明确选择每一页一次。' }
      pages.push(page)
    }
  }
  return { pages: pages.sort((left, right) => left - right) }
}

export function PdfContentEditor({ value, pageCount, availability, disabled, onChange, onValidityChange }: Props) {
  const options = value ?? empty
  const [pageRange, setPageRange] = useState(() => value?.ocr?.pages.join(', ') ?? (pageCount > 1 ? `1-${pageCount}` : '1'))
  const parsed = pagesFrom(pageRange, pageCount)
  const error = options.ocr ? parsed.error : undefined
  const valid = !error && (!options.ocr || availability.ocr) && (!options.embedStandardFonts || availability.fonts)
    && (!options.targetVersion || availability.version)
  useEffect(() => { onValidityChange(valid) }, [onValidityChange, valid])
  const update = (changes: Partial<ContentOptions>) => {
    const next = { ...options, ...changes }
    onChange(next.ocr || next.embedStandardFonts || next.targetVersion ? next : null)
  }
  return <fieldset disabled={disabled} className="rounded border p-3 space-y-3" aria-label="PDF 内容处理选项">
    <legend>内容处理</legend>
    {availability.reason && <p>{availability.reason}</p>}
    <label className="block"><input type="checkbox" aria-label="对扫描页进行 OCR" checked={!!options.ocr}
      disabled={disabled || !availability.ocr || pageCount < 1 || pageCount > 500}
      onChange={event => update({ ocr: event.target.checked ? { language: 'eng', pages: parsed.pages, dpi: 150 } : null })} /> 对扫描页进行 OCR</label>
    {options.ocr && <div className="space-y-2">
      <p>识别语言：英语；分辨率：150 DPI。已有可搜索文字的页会跳过。采用前请核对识别文本和阅读顺序。</p>
      <label>页范围 <input aria-label="OCR 页范围" value={pageRange} placeholder="例如 1, 3-5" onChange={event => {
        const text = event.target.value; setPageRange(text)
        update({ ocr: { language: 'eng', pages: pagesFrom(text, pageCount).pages, dpi: 150 } })
      }} /></label>
      {error ? <p role="alert">{error}</p> : <p>已选择 {parsed.pages.length} 页：{parsed.pages.join(', ')}</p>}
    </div>}
    <label className="block"><input type="checkbox" aria-label="嵌入支持的标准字体" checked={options.embedStandardFonts}
      disabled={disabled || !availability.fonts} onChange={event => update({ embedStandardFonts: event.target.checked })} /> 嵌入支持的标准字体</label>
    <p>支持 Helvetica、Times、Courier 的 12 种标准字形和 WinAnsi 编码。其它缺失字体会阻止此操作。</p>
    <label className="block"><input type="checkbox" aria-label="输出为 PDF 1.7" checked={options.targetVersion === '1.7'}
      disabled={disabled || !availability.version} onChange={event => update({ targetVersion: event.target.checked ? '1.7' : null })} /> 输出为 PDF 1.7</label>
    <p>版本处理支持已验证的 PDF 1.x 文件；PDF/A 和区域合规状态仍需单独验证。</p>
  </fieldset>
}
