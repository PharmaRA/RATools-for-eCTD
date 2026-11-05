import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { apiFetch } from '../../apiClient'
import type { AssemblySourcePage, AssemblySourceUpload } from '../../api/generated'

type NodeChoice = { id: string; label: string }
type Source = { uploaded: AssemblySourceUpload; selected: boolean; nodeId: string; fileName: string; title: string }
type Props = { appId: string; seqNumber: string; expectedRevision?: number; disabled: boolean;
  nodes: readonly NodeChoice[]; onManifest: (manifest: string) => void }
const cell = (value: string | number) => `"${String(value).replaceAll('"', '""')}"`
const message = (error: unknown) => error instanceof Error ? error.message : '来源上传失败'

export function AssemblySourcesPanel({ appId, seqNumber, expectedRevision, disabled, nodes, onManifest }: Props) {
  const base = `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seqNumber)}/assembly-sources`
  const [sources, setSources] = useState<Source[]>([])
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  const [page, setPage] = useState(0)
  const [nextOffset, setNextOffset] = useState<number>()
  const alive = useRef(true)
  const readOnly = useRef(disabled)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  useLayoutEffect(() => { readOnly.current = disabled }, [disabled])
  const selected = sources.filter(source => source.selected)
  const usable = !disabled && !busy && selected.length > 0 && selected.length <= 1000
    && selected.every(source => nodes.some(node => node.id === source.nodeId) && source.fileName.trim())
  const merge = (values: AssemblySourceUpload[]) => {
    if (!Array.isArray(values) || values.length > 1000 || values.some(value => !value.documentId || value.sourceFile !== `document:${value.documentId}`
      || !value.fileName || !Number.isSafeInteger(value.length) || value.length < 0 || !/^[a-f0-9]{64}$/i.test(value.sha256)))
      throw new Error('来源返回值不完整或数量超限，请重新读取。')
    if (!alive.current) return
    setSources(current => {
      const previous = new Map(current.map(item => [item.uploaded.documentId, item]))
      let selectedCount = current.filter(item => item.selected).length
      for (const uploaded of values) {
        const old = previous.get(uploaded.documentId)
        if (old && old.uploaded.sha256 !== uploaded.sha256) continue
        const selected = selectedCount < 1000
        if (!old && selected) selectedCount++
        previous.set(uploaded.documentId, old ? { ...old, uploaded } : { uploaded, selected, nodeId: '', fileName: uploaded.fileName, title: '' })
      }
      return [...previous.values()]
    })
  }
  const discover = async (offset = 0) => {
    if (busy) return
    setBusy(true); setErrors([])
    try {
      const result: AssemblySourcePage = await apiFetch(`${base}?offset=${offset}&limit=1000`)
      if (result.offset !== offset || result.limit !== 1000 || !Number.isSafeInteger(result.total)
        || !Array.isArray(result.items) || result.items.length > 1000 || result.total < offset + result.items.length
        || (result.items.length === 0 && result.total > offset)) throw new Error('来源分页返回值不匹配，请重新读取。')
      merge(result.items)
      if (alive.current) setNextOffset(offset + result.items.length < result.total ? offset + result.items.length : undefined)
    }
    catch (caught) { if (alive.current) setErrors([message(caught)]) }
    finally { if (alive.current) setBusy(false) }
  }
  const upload = async (files: File[]) => {
    if (disabled || busy || expectedRevision === undefined || files.length === 0) return
    if (files.length > 1000) { setErrors(['一次最多上传 1000 个来源文件。']); return }
    setBusy(true); setErrors([])
    for (const file of files) {
      if (!alive.current || readOnly.current) break
      try {
        if (file.size > 512 * 1024 * 1024) throw new Error('文件超出 512 MiB 限制')
        const body = new FormData(); body.append('file', file); body.append('expectedRevision', String(expectedRevision))
        const uploaded: AssemblySourceUpload = await apiFetch(base, { method: 'POST', body })
        if (uploaded.expectedRevision !== expectedRevision) throw new Error('来源登记的草稿修订不匹配，请刷新后读取已上传来源。')
        merge([uploaded])
      } catch (caught) { if (alive.current) setErrors(current => [...current, `${file.name}：${message(caught)}`]) }
    }
    if (alive.current) setBusy(false)
  }
  const change = (id: string, changes: Partial<Omit<Source, 'uploaded'>>) =>
    setSources(current => current.map(source => source.uploaded.documentId === id ? { ...source, ...changes } : source))
  const generate = () => {
    if (!usable) return
    const lines = ['sourceFile,nodeInstanceId,operation,title,fileName,targetLeafAddress,sortOrder']
    const orders = new Map<string, number>()
    for (const source of selected) {
      const order = orders.get(source.nodeId) ?? 0; orders.set(source.nodeId, order + 1)
      lines.push([source.uploaded.sourceFile, source.nodeId, 'new', source.title, source.fileName, '', order].map(cell).join(','))
    }
    onManifest(lines.join('\r\n'))
  }
  return <details className="rounded border p-3"><summary>批量上传与映射来源文件</summary>
    <p>上传只登记来源；每份文件需明确目标节点。生成清单后仍须预览、处理问题并确认执行。</p>
    <div className="flex gap-3"><label>源文件 <input type="file" multiple aria-label="上传装配源文件" disabled={disabled || busy || expectedRevision === undefined}
      onChange={event => { const files = Array.from(event.currentTarget.files ?? []); event.currentTarget.value = ''; void upload(files) }} /></label>
      <button type="button" disabled={busy} onClick={() => { void discover() }}>读取已上传装配来源</button>
      <button type="button" disabled={busy || nextOffset === undefined} onClick={() => { if (nextOffset !== undefined) void discover(nextOffset) }}>加载更多装配来源</button></div>
    {busy && <p role="status">正在登记或读取来源…</p>}
    {errors.map((error, index) => <p role="alert" key={index}>{error}</p>)}
    <p>已读取 {sources.length} 个来源，选择 {selected.length} 个。来源 token 在响应丢失后可重新读取。</p>
    {sources.length > 0 && <div className="overflow-auto"><table><thead><tr><th>选择 / 来源</th><th>目标节点</th><th>目标文件名 / 标题</th></tr></thead>
      <tbody>{sources.slice(page * 10, page * 10 + 10).map(source => <tr key={source.uploaded.documentId}>
        <td><label><input type="checkbox" aria-label={`选择来源 ${source.uploaded.documentId}`} checked={source.selected} disabled={disabled || busy || (!source.selected && selected.length >= 1000)}
          onChange={event => change(source.uploaded.documentId, { selected: event.target.checked })} />{source.uploaded.fileName}</label>
          <br /><code>{source.uploaded.sourceFile}</code></td>
        <td><select aria-label={`来源目标节点 ${source.uploaded.documentId}`} value={source.nodeId} disabled={disabled || busy}
          onChange={event => change(source.uploaded.documentId, { nodeId: event.target.value })}><option value="">明确选择业务节点</option>
          {nodes.map(node => <option key={node.id} value={node.id}>{node.label}</option>)}</select></td>
        <td><input aria-label={`来源文件名 ${source.uploaded.documentId}`} value={source.fileName} disabled={disabled || busy}
          onChange={event => change(source.uploaded.documentId, { fileName: event.target.value })} />
          <input aria-label={`来源标题 ${source.uploaded.documentId}`} value={source.title} disabled={disabled || busy}
            onChange={event => change(source.uploaded.documentId, { title: event.target.value })} /></td>
      </tr>)}</tbody></table></div>}
    <div className="flex gap-3"><button type="button" disabled={page === 0} onClick={() => setPage(page - 1)}>上一页来源</button>
      <span>第 {page + 1} 页</span><button type="button" disabled={(page + 1) * 10 >= sources.length} onClick={() => setPage(page + 1)}>下一页来源</button>
      <button type="button" disabled={!usable} onClick={generate}>将所选来源填入清单</button></div>
  </details>
}
