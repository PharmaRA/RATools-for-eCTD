import { useEffect } from 'react'
import type { PdfLinkEdit, PdfLinkInspection, PdfLinkTargetKind } from '../../api/generated'

export type LinkKind = PdfLinkTargetKind
export type NavigationLink = PdfLinkInspection
export type LinkEdit = Omit<PdfLinkEdit, 'objectPath' | 'originalTarget'> & {
  objectPath: string
  originalTarget: string
}
export type RemotePdfTarget = {
  id: string
  label: string
  fileSpecification: string
  placementId: string | null
  versionId: string | null
  namedDestinations: readonly string[] | null
}
type PdfLinksEditorProps = {
  original: readonly NavigationLink[]
  value: readonly LinkEdit[]
  pageCount: number
  namedDestinations: readonly string[]
  remoteTargets: readonly RemotePdfTarget[]
  disabled?: boolean
  onChange: (edits: readonly LinkEdit[]) => void
  onValidityChange: (valid: boolean) => void
}
const originalTarget = (link: NavigationLink) => link.target
type RemoteFragment = { mode: 'page' | 'named'; value: string }
const readRemoteFragment = (target: string): RemoteFragment | null => {
  const fragment = target.slice(target.indexOf('#') + 1)
  if (!target.includes('#')) return null
  if (/^page=\d*$/.test(fragment)) return { mode: 'page', value: fragment.slice('page='.length) }
  if (fragment.startsWith('nameddest=')) return { mode: 'named', value: fragment.slice('nameddest='.length) }
  return null
}

export function PdfLinksEditor({ original, value, pageCount, namedDestinations, remoteTargets, disabled = false,
  onChange, onValidityChange }: PdfLinksEditorProps) {
  const remoteFor = (edit: LinkEdit) => remoteTargets.find(target => target.placementId === edit.targetPlacementId
    && target.versionId === edit.targetVersionId && target.fileSpecification === edit.target.split('#', 1)[0])
  const problemFor = (edit: LinkEdit): string | null => {
    if (!original.some(link => link.objectPath === edit.objectPath && link.sourcePageNumber === edit.sourcePageNumber
      && originalTarget(link) === edit.originalTarget)) return '原链接已变化，请撤销后重新编辑'
    switch (edit.targetKind) {
      case 0: return /^\d+$/.test(edit.target) && Number(edit.target) >= 1 && Number(edit.target) <= pageCount
        ? null : `页码必须在 1–${pageCount} 之间`
      case 1: return namedDestinations.includes(edit.target) ? null : '请选择已存在的命名目的地'
      case 2: {
        const target = remoteFor(edit)
        if (!target || (!target.placementId && !target.versionId)) return '请选择明确的目标文件'
        const fragment = readRemoteFragment(edit.target)
        if (!fragment || !fragment.value.trim()) return '请选择远程页码或命名目的地'
        if (fragment.mode === 'page') {
          return Number(fragment.value) >= 1 ? null : '远程页码必须是正整数'
        }
        return target.namedDestinations === null || target.namedDestinations.includes(fragment.value)
          ? null : '远程命名目的地无法解析'
      }
      case 3:
        try { return ['https:', 'http:', 'mailto:'].includes(new URL(edit.target).protocol) ? null : '网址必须使用 HTTP、HTTPS 或 mailto' }
        catch { return '请输入完整网址' }
    }
  }
  const valid = value.every(edit => problemFor(edit) === null)
    && new Set(value.map(edit => edit.objectPath)).size === value.length
  useEffect(() => { onValidityChange(valid) }, [valid, onValidityChange])
  const update = (edit: LinkEdit, change: Partial<LinkEdit>) => {
    if (!disabled) onChange(value.map(item => item.objectPath === edit.objectPath ? { ...edit, ...change } : item))
  }
  const start = (link: NavigationLink) => {
    const target = originalTarget(link)
    onChange([...value, { sourcePageNumber: link.sourcePageNumber, objectPath: link.objectPath,
      originalTarget: target, targetKind: link.kind, target: link.kind === 2 ? '' : link.target,
      targetPlacementId: null, targetVersionId: null }])
  }
  return <section aria-label="PDF 链接" className="space-y-3">
    <fieldset disabled={disabled} className="space-y-3">
      <legend className="font-medium">链接目标</legend>
      {value.filter(edit => !original.some(link => link.objectPath === edit.objectPath)).map(edit =>
        <div key={edit.objectPath}>
          <p role="alert">原链接已变化：第 {edit.sourcePageNumber} 页，原目标 {edit.originalTarget}。该修改尚未撤销。</p>
          <button type="button" onClick={() => onChange(value.filter(item => item.objectPath !== edit.objectPath))}>撤销失效链接修改</button>
        </div>)}
      {original.length === 0 && <p>未发现可编辑的页面链接。</p>}
      <ol className="space-y-3">{original.map((link, index) => {
        const edit = value.find(item => item.objectPath === link.objectPath)
        const remote = edit && remoteFor(edit)
        const error = edit && problemFor(edit)
        return <li key={link.objectPath} className="rounded border p-3 space-y-2">
          <p>链接 {index + 1} · 第 {link.sourcePageNumber} 页 · 原目标：{link.fileSpecification
            ? `${link.fileSpecification}#${link.namedDestination ?? link.target}` : link.target}
          {!link.isValid && '（原目标无法解析）'}</p>
          {!edit ? <button type="button" onClick={() => start(link)}>修改链接 {index + 1}</button>
            : <>
              <div className="flex flex-wrap gap-3">
                <label>目标类型 <select aria-label={`链接 ${index + 1} 目标类型`} value={edit.targetKind}
                  onChange={event => update(edit, { targetKind: Number(event.target.value) as LinkKind,
                    target: event.target.value === '0' ? '1' : '', targetPlacementId: null, targetVersionId: null })}>
                  <option value={0}>本文件页码</option><option value={1}>本文件命名目的地</option>
                  <option value={2}>其他 PDF 文件</option><option value={3}>外部网址</option>
                </select></label>
                {edit.targetKind === 0 && <label>页码 <input type="number" min={1} max={pageCount} step={1}
                  aria-label={`链接 ${index + 1} 页码`} value={edit.target} onChange={event => update(edit, { target: event.target.value })} /></label>}
                {edit.targetKind === 1 && <label>命名目的地 <select aria-label={`链接 ${index + 1} 命名目的地`}
                  value={edit.target} onChange={event => update(edit, { target: event.target.value })}>
                  <option value="">请选择目的地</option>{namedDestinations.map(name => <option key={name} value={name}>{name}</option>)}
                </select></label>}
                {edit.targetKind === 2 && <>
                  <label>目标文件 <select aria-label={`链接 ${index + 1} 目标文件`} value={remote?.id ?? ''}
                    onChange={event => {
                      const selected = remoteTargets.find(target => target.id === event.target.value)
                      update(edit, { target: selected?.fileSpecification ?? '', targetPlacementId: selected?.placementId ?? null,
                        targetVersionId: selected?.versionId ?? null })
                    }}>
                    <option value="">请选择目标文件</option>{remoteTargets.map(target => <option key={target.id} value={target.id}>{target.label}</option>)}
                  </select></label>
                  {remote && <>
                    <label>远程目标模式 <select aria-label={`链接 ${index + 1} 远程目标模式`}
                      value={readRemoteFragment(edit.target)?.mode ?? 'page'}
                      onChange={event => update(edit, { target: `${remote.fileSpecification}#${event.target.value === 'page' ? 'page=' : 'nameddest='}` })}>
                      <option value="page">目标页码</option><option value="named">命名目的地</option>
                    </select></label>
                    {(readRemoteFragment(edit.target)?.mode ?? 'page') === 'page'
                      ? <label>远程页码 <input type="number" min={1} step={1} aria-label={`链接 ${index + 1} 远程页码`}
                        value={readRemoteFragment(edit.target)?.value ?? ''}
                        onChange={event => update(edit, { target: `${remote.fileSpecification}#page=${event.target.value}` })} /></label>
                      : remote.namedDestinations !== null
                        ? <label>远程目的地 <select aria-label={`链接 ${index + 1} 远程目的地`}
                          value={readRemoteFragment(edit.target)?.value ?? ''}
                          onChange={event => update(edit, { target: `${remote.fileSpecification}#nameddest=${event.target.value}` })}>
                          <option value="">请选择目的地</option>{remote.namedDestinations.map(name => <option key={name} value={name}>{name}</option>)}
                        </select></label>
                        : <label>远程命名目的地
                          <input aria-label={`链接 ${index + 1} 远程命名目的地`}
                            value={readRemoteFragment(edit.target)?.value ?? ''}
                            onChange={event => update(edit, { target: `${remote.fileSpecification}#nameddest=${event.target.value}` })} />
                          <span>生成计划时校验目标版本和目的地。</span>
                        </label>}
                  </>}
                </>}
                {edit.targetKind === 3 && <label>网址 <input aria-label={`链接 ${index + 1} 网址`} value={edit.target}
                  onChange={event => update(edit, { target: event.target.value })} /></label>}
              </div>
              {error && <p role="alert">链接 {index + 1}：{error}</p>}
              <button type="button" onClick={() => onChange(value.filter(item => item.objectPath !== link.objectPath))}>保留链接 {index + 1}</button>
            </>}
        </li>
      })}</ol>
    </fieldset>
  </section>
}
