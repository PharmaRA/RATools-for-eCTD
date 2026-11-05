import { useEffect } from 'react'
import type { PdfBookmarkEdit } from '../../api/generated'

export type BookmarkEdit = PdfBookmarkEdit

type PdfBookmarksEditorProps = {
  original: readonly BookmarkEdit[]
  value: readonly BookmarkEdit[] | null
  pageCount: number
  namedDestinations: readonly string[]
  disabled?: boolean
  onChange: (bookmarks: readonly BookmarkEdit[] | null) => void
  onValidityChange: (valid: boolean) => void
}

export function PdfBookmarksEditor({ original, value, pageCount, namedDestinations, disabled = false,
  onChange, onValidityChange }: PdfBookmarksEditorProps) {
  const rows = value ?? original
  const problems = value?.map((bookmark, index) => {
    if (!bookmark.title.trim()) return '请输入书签标题'
    if (!Number.isInteger(bookmark.level) || bookmark.level < 0 || bookmark.level > 32)
      return '层级必须是 0–32 之间的整数'
    if ((index === 0 && bookmark.level !== 0) || (index > 0 && bookmark.level > value[index - 1].level + 1))
      return '层级不能跳过父级；首个书签的层级必须为 0'
    if (bookmark.namedDestination !== null) {
      if (bookmark.pageNumber !== null) return '请选择一种目标：页码或命名目的地'
      if (!namedDestinations.includes(bookmark.namedDestination)) return '请选择已存在的命名目的地'
    } else if (bookmark.pageNumber === null || !Number.isInteger(bookmark.pageNumber)
      || bookmark.pageNumber < 1 || bookmark.pageNumber > pageCount) return `页码必须在 1–${pageCount} 之间`
    return null
  }) ?? []
  const valid = problems.every(problem => problem === null)
  useEffect(() => { onValidityChange(valid) }, [valid, onValidityChange])

  const update = (index: number, change: Partial<BookmarkEdit>) => {
    if (value !== null && !disabled) onChange(value.map((row, position) => position === index ? { ...row, ...change } : row))
  }
  const move = (index: number, step: number) => {
    if (value === null || disabled || index + step < 0 || index + step >= value.length) return
    const next = [...value]
    ;[next[index], next[index + step]] = [next[index + step], next[index]]
    onChange(next)
  }
  return <section aria-label="PDF 书签" className="space-y-3">
    <p>层级 0 表示顶层书签；子项紧随父项，层级增加 1。调整顺序后请核对层级和目标。</p>
    <fieldset disabled={disabled} className="space-y-3">
      <legend className="font-medium">书签</legend>
      {value === null
        ? <button type="button" onClick={() => onChange(original.map(row => ({ ...row })))}>编辑书签</button>
        : <div className="flex gap-3">
          <button type="button" onClick={() => onChange(null)}>保留原书签</button>
          <button type="button" onClick={() => onChange([...value, { title: '', level: 0, pageNumber: 1, namedDestination: null }])}>添加书签</button>
          <button type="button" onClick={() => onChange([])}>清空全部书签</button>
        </div>}
      {rows.length === 0 && <p>{value === null ? '原文件没有书签。' : '处理结果将不包含书签。'}</p>}
      <ol className="space-y-3">
        {rows.map((bookmark, index) => <li key={index} className="rounded border p-3 space-y-2">
          {value === null ? <span>{bookmark.title} · 层级 {bookmark.level} · {bookmark.namedDestination !== null
            ? `目的地 ${bookmark.namedDestination}` : `第 ${bookmark.pageNumber ?? '未知'} 页`}</span>
            : <>
              <div className="flex flex-wrap gap-3">
                <label>标题 <input aria-label={`书签 ${index + 1} 标题`} value={bookmark.title}
                  onChange={event => update(index, { title: event.target.value })} /></label>
                <label>层级 <input type="number" min={0} max={32} step={1} aria-label={`书签 ${index + 1} 层级`}
                  value={bookmark.level < 0 ? '' : bookmark.level}
                  onChange={event => update(index, { level: event.target.value === '' ? -1 : Number(event.target.value) })} /></label>
                <label>目标类型 <select aria-label={`书签 ${index + 1} 目标类型`}
                  value={bookmark.namedDestination === null ? 'page' : 'named'} onChange={event => update(index,
                    event.target.value === 'page' ? { pageNumber: 1, namedDestination: null } : { pageNumber: null, namedDestination: '' })}>
                  <option value="page">页码</option><option value="named">命名目的地</option>
                </select></label>
                {bookmark.namedDestination === null
                  ? <label>页码 <input type="number" min={1} max={pageCount} step={1} aria-label={`书签 ${index + 1} 页码`}
                    value={bookmark.pageNumber ?? ''} onChange={event => update(index,
                      { pageNumber: event.target.value === '' ? null : Number(event.target.value) })} /></label>
                  : <label>命名目的地 <select aria-label={`书签 ${index + 1} 命名目的地`} value={bookmark.namedDestination}
                    onChange={event => update(index, { namedDestination: event.target.value })}>
                    <option value="">请选择目的地</option>
                    {!namedDestinations.includes(bookmark.namedDestination) && bookmark.namedDestination !== ''
                      && <option value={bookmark.namedDestination}>{bookmark.namedDestination}（无法解析）</option>}
                    {namedDestinations.map(name => <option key={name} value={name}>{name}</option>)}
                  </select></label>}
              </div>
              {problems[index] && <p role="alert">书签 {index + 1}：{problems[index]}</p>}
              <div className="flex gap-3">
                <button type="button" disabled={index === 0} onClick={() => move(index, -1)}>上移书签 {index + 1}</button>
                <button type="button" disabled={index === rows.length - 1} onClick={() => move(index, 1)}>下移书签 {index + 1}</button>
                <button type="button" onClick={() => onChange(value.filter((_, position) => position !== index))}>移除书签 {index + 1}</button>
              </div>
            </>}
        </li>)}
      </ol>
    </fieldset>
  </section>
}
