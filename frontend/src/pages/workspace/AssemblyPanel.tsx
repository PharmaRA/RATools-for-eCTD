import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ApiRequestError, apiFetch, buildJsonRequestInit } from '../../apiClient'
import type { AssemblyExecutionJobView, AssemblyPreview } from '../../api/generated'
import { AssemblySourcesPanel } from './AssemblySourcesPanel'

type Props = { appId: string; seqNumber: string; workspaceRevision?: number; disabled: boolean;
  nodes?: readonly { id: string; label: string }[]; onExecuted: () => Promise<void> | void }
type Handle = { planId: string; planDigest: string; idempotencyKey: string; expectedRevision: number; jobId?: string }
const same = (left: Handle | undefined, right: Handle) => !!left && left.planId === right.planId
  && left.planDigest === right.planDigest && left.idempotencyKey === right.idempotencyKey
const message = (error: unknown) => error instanceof Error ? error.message : '装配操作失败'
const states = ['排队中', '准备与验证文件', '装配已提交', '装配失败', '已取消', '处理超时']
function read(key: string): Handle | undefined {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(key) ?? 'null')
    if (!value || typeof value !== 'object') return undefined
    const item = value as Partial<Handle>
    return typeof item.planId === 'string' && typeof item.planDigest === 'string' && typeof item.idempotencyKey === 'string'
      && Number.isSafeInteger(item.expectedRevision) && (item.jobId === undefined || typeof item.jobId === 'string') ? item as Handle : undefined
  } catch { return undefined }
}
const sorted = (rows: readonly number[]) => [...rows].sort((a, b) => a - b)
const equalRows = (left: readonly number[], right: readonly number[]) => JSON.stringify(sorted(left)) === JSON.stringify(sorted(right))
const sha256 = async (bytes: Uint8Array) => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes as Uint8Array<ArrayBuffer>)))
  .map(value => value.toString(16).padStart(2, '0')).join('')
function csvCell(value: unknown): string {
  let text = String(value ?? '')
  if (/^[\s]*[=+@-]/.test(text)) text = `'${text}`
  return `"${text.replaceAll('"', '""')}"`
}
function download(name: string, csv: string) {
  const url = URL.createObjectURL(new Blob(['\ufeff', csv], { type: 'text/csv;charset=utf-8' }))
  try { const link = document.createElement('a'); link.href = url; link.download = name; link.click() }
  finally { setTimeout(() => URL.revokeObjectURL(url), 1000) }
}
export const AssemblyPanel = (props: Props) => <AssemblyEditor key={JSON.stringify([props.appId, props.seqNumber])} {...props} />

function AssemblyEditor({ appId, seqNumber, workspaceRevision, disabled, nodes = [], onExecuted }: Props) {
  const base = `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seqNumber)}`
  const storageKey = `ratools.assembly:${JSON.stringify([appId, seqNumber])}`
  const [manifest, setManifest] = useState('')
  const [workbook, setWorkbook] = useState<{ name: string; data: string }>()
  const [preview, setPreview] = useState<AssemblyPreview>()
  const [selected, setSelected] = useState<number[]>([])
  const [handle, setHandle] = useState(() => read(storageKey))
  const [job, setJob] = useState<AssemblyExecutionJobView>()
  const [busy, setBusy] = useState(false)
  const [fileBusy, setFileBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [pollError, setPollError] = useState<string>()
  const [page, setPage] = useState(0)
  const generation = useRef(0)
  const commands = useRef(0)
  const alive = useRef(true)
  const readOnly = useRef(disabled)
  const notified = useRef<string | undefined>(undefined)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  useLayoutEffect(() => { readOnly.current = disabled }, [disabled])
  useLayoutEffect(() => {
    // A new revision invalidates editable inputs, but a committed task must survive
    // the parent's completion refresh with its verified result and notification fence.
    generation.current++
    setManifest(''); setWorkbook(undefined); setPreview(undefined); setSelected([])
    setError(undefined); setPage(0); setFileBusy(false)
  }, [workspaceRevision])
  const running = job?.status === 0 || job?.status === 1
  const editable = !disabled && !busy && !fileBusy && !handle
  const selectionDirty = !!preview && !equalRows(selected, preview.selectedRows)
  const canExecute = editable && !!preview?.canExecute && !selectionDirty && selected.length > 0
  const invalidate = () => { generation.current++; setPreview(undefined); setSelected([]); setError(undefined); setPage(0) }
  const persist = (value: Handle) => { localStorage.setItem(storageKey, JSON.stringify(value)); if (alive.current) setHandle(value) }
  const receive = async (value: AssemblyExecutionJobView, request: Handle) => {
    if (value.planId !== request.planId || (request.jobId && value.id !== request.jobId)) throw new Error('装配任务身份与已保存请求不一致。')
    if (value.status === 2) {
      const result = value.result
      if (!result || result.jobId !== value.id || result.planId !== request.planId || result.planDigest !== request.planDigest
        || result.applicationId !== appId || result.sequenceNumber !== seqNumber || result.expectedRevision !== request.expectedRevision
        || result.workspaceRevision !== request.expectedRevision + 1 || !Array.isArray(result.documents))
        throw new Error('装配完成证据与当前请求不一致，请重新查询。')
    }
    if (!same(read(storageKey), request)) return
    persist({ ...request, jobId: value.id })
    if (alive.current) setJob(value)
    if (value.status === 2) {
      if (notified.current !== value.id) { notified.current = value.id; await onExecuted() }
    }
  }
  const validatePreview = (value: AssemblyPreview, manifestHash: string, format: string) => {
    if (value.plan.applicationId !== appId || value.plan.sequenceNumber !== seqNumber || value.plan.expectedRevision !== workspaceRevision
      || !value.id || !value.plan.planDigest || !Array.isArray(value.selectedRows)) throw new Error('装配预览与当前工作区修订不一致。')
    if (value.manifestSha256 !== manifestHash || value.format !== format)
      throw new Error('装配预览与本次清单内容不一致，请重新生成预览。')
    if (new Set(value.selectedRows).size !== value.selectedRows.length || value.selectedRows.some(line =>
      !value.plan.rows.some(row => row.lineNumber === line && row.status === 0)))
      throw new Error('装配预览包含未确认的无效行。')
    return value
  }
  const generate = async () => {
    if (!editable || workspaceRevision === undefined || (!workbook && !manifest.trim())) return
    setBusy(true); setError(undefined); setPreview(undefined)
    const current = ++generation.current
    try {
      const format = workbook ? 'xlsx' : 'csv'
      const bytes = workbook ? Uint8Array.from(atob(workbook.data), value => value.charCodeAt(0)) : new TextEncoder().encode(manifest)
      const manifestHash = await sha256(bytes)
      if (!alive.current || current !== generation.current) return
      const value = validatePreview(await apiFetch(`${base}/assembly-plans`, buildJsonRequestInit('POST', {
        expectedRevision: workspaceRevision, manifest: workbook ? '' : manifest, format,
        ...(workbook ? { manifestBase64: workbook.data } : {}),
      })), manifestHash, format)
      if (!alive.current || current !== generation.current) return
      setPreview(value); setSelected([...value.selectedRows]); setPage(0)
    } catch (caught) { if (alive.current && current === generation.current) setError(message(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const selectSubset = async () => {
    if (!editable || !preview || selected.length === 0) return
    const original = preview
    const requested = sorted(selected)
    setBusy(true); setError(undefined)
    const current = ++generation.current
    try {
      const value = validatePreview(await apiFetch(`${base}/assembly-plans/${encodeURIComponent(original.id)}/selection`, buildJsonRequestInit('POST', {
        expectedRevision: workspaceRevision, parentDigest: original.plan.planDigest, selectedRows: requested,
      })), original.manifestSha256, original.format)
      if (!alive.current || current !== generation.current) return
      if (value.id === original.id || value.plan.planDigest === original.plan.planDigest || !equalRows(value.selectedRows, requested))
        throw new Error('所选行未形成新的完整计划，请重新预览。')
      setPreview(value); setSelected([...value.selectedRows])
    } catch (caught) { if (alive.current && current === generation.current) setError(message(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const enqueue = async (request: Handle) => {
    const value: AssemblyExecutionJobView = await apiFetch(`${base}/assembly-executions`, buildJsonRequestInit('POST', {
      planId: request.planId, planDigest: request.planDigest, idempotencyKey: request.idempotencyKey,
    }))
    await receive(value, request)
  }
  const execute = async () => {
    if (!canExecute || !preview) return
    const request: Handle = { planId: preview.id, planDigest: preview.plan.planDigest, idempotencyKey: crypto.randomUUID(), expectedRevision: preview.plan.expectedRevision }
    setBusy(true); setError(undefined)
    try {
      const saved = read(storageKey)
      if (saved && !same(saved, request)) throw new Error('已有较新的装配任务，请重新打开工作区。')
      persist(request)
      await enqueue(request)
    } catch (caught) {
      if (caught instanceof ApiRequestError && [400, 404, 409].includes(caught.status) && same(read(storageKey), request)) {
        localStorage.setItem(storageKey, 'null'); if (alive.current) { setHandle(undefined); invalidate() }
      }
      if (alive.current) setError(message(caught))
    } finally { if (alive.current) setBusy(false) }
  }
  const recover = async () => {
    if (!handle || busy) return
    setBusy(true); setError(undefined)
    try {
      const value: AssemblyExecutionJobView = await apiFetch(`${base}/assembly-plans/${encodeURIComponent(handle.planId)}/execution`)
      await receive(value, handle)
    } catch (caught) {
      if (caught instanceof ApiRequestError && caught.status === 404 && same(read(storageKey), handle)) {
        if (!alive.current || readOnly.current) { if (alive.current) setError('当前工作区为只读，未找到已创建任务，不能重新入队。'); return }
        try { await enqueue(handle) }
        catch (retryError) {
          if (retryError instanceof ApiRequestError && [400, 404, 409].includes(retryError.status) && same(read(storageKey), handle)) {
            localStorage.setItem(storageKey, 'null')
            if (alive.current) { setHandle(undefined); setJob(undefined); invalidate() }
          }
          if (alive.current) setError(message(retryError))
        }
      } else if (alive.current) setError(message(caught))
    } finally { if (alive.current) setBusy(false) }
  }
  const refresh = async (cancel = false) => {
    if (busy || !handle?.jobId || (cancel && (disabled || !running))) return
    setBusy(true); setError(undefined); commands.current++
    try {
      const value: AssemblyExecutionJobView = await apiFetch(`${base}/assembly-executions/${encodeURIComponent(handle.jobId)}${cancel ? '/cancel' : ''}`,
        cancel ? { method: 'POST' } : undefined)
      await receive(value, handle)
    } catch (caught) { if (alive.current) setError(message(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  useEffect(() => {
    if (!running || !handle?.jobId) return
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>
    const poll = async () => {
      const current = commands.current
      try {
        const value: AssemblyExecutionJobView = await apiFetch(`${base}/assembly-executions/${encodeURIComponent(handle.jobId!)}`)
        if (!cancelled && alive.current && current === commands.current) { await receive(value, handle); setPollError(undefined) }
      } catch (caught) { if (!cancelled && alive.current) setPollError(message(caught)) }
      if (!cancelled) timer = setTimeout(() => { void poll() }, 1500)
    }
    timer = setTimeout(() => { void poll() }, 1500)
    return () => { cancelled = true; clearTimeout(timer) }
    // Receive intentionally uses this scope's application callback and request.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [base, handle?.jobId, running])
  const reset = () => {
    if (busy || running || (handle && !job)) return
    if (handle && !same(read(storageKey), handle)) { setError('发现较新的装配任务，请刷新工作区。'); return }
    localStorage.setItem(storageKey, 'null'); setHandle(undefined); setJob(undefined); setPollError(undefined); invalidate()
  }
  const loadFile = async (file: File | undefined) => {
    if (!file || !editable) return
    invalidate(); setFileBusy(true)
    const current = generation.current
    try {
      if (file.size > 2 * 1024 * 1024) throw new Error('装配清单不得超过 2 MiB。')
      if (!/\.(csv|xlsx)$/i.test(file.name)) throw new Error('请选择 CSV 或 XLSX 数据清单。')
      const bytes = new Uint8Array(await file.arrayBuffer())
      if (!alive.current || current !== generation.current) return
      if (/\.csv$/i.test(file.name)) { setManifest(new TextDecoder('utf-8', { fatal: true }).decode(bytes)); setWorkbook(undefined) }
      else {
        let binary = ''
        for (const value of bytes) binary += String.fromCharCode(value)
        setWorkbook({ name: file.name, data: btoa(binary) }); setManifest('')
      }
    } catch (caught) { if (alive.current && current === generation.current) setError(message(caught)) }
    finally { if (alive.current) setFileBusy(false) }
  }
  const exportIssues = () => {
    if (!preview) return
    const lines: unknown[][] = [['lineNumber', 'sourceFile', 'fileName', 'nodeInstanceId', 'code', 'message']]
    for (const issue of preview.plan.diagnostics) lines.push([issue.lineNumber, '', '', '', issue.code, issue.message])
    for (const row of preview.plan.rows) for (const issue of row.diagnostics)
      lines.push([row.lineNumber, row.sourceFile ?? row.documentId, row.fileName, row.nodeInstanceId, issue.code, issue.message])
    download('assembly-diagnostics.csv', lines.map(line => line.map(csvCell).join(',')).join('\r\n'))
  }
  return <section aria-label="批量装配" className="rounded border bg-white p-4 space-y-3 mb-3">
    <h3 className="font-medium">批量装配（CSV / XLSX）</h3>
    <p>来源使用已上传的 document:文档ID；每行明确业务节点。先查看全部问题，再确认有效子集。整批结果原子提交，源文件与定稿保留。</p>
    {error && <p role="alert">{error}</p>}{pollError && <p role="alert">{pollError}</p>}
    {nodes.length > 0 && <details><summary>查看可用目标业务节点</summary><ul>{nodes.map(node => <li key={node.id}>
      {node.label} · <code>{node.id}</code></li>)}</ul></details>}
    <AssemblySourcesPanel key={workspaceRevision} appId={appId} seqNumber={seqNumber} expectedRevision={workspaceRevision}
      disabled={!editable} nodes={nodes} onManifest={value => { setManifest(value); setWorkbook(undefined); invalidate() }} />
    <div className="flex gap-3"><button type="button" onClick={() => download('assembly-template.csv',
      'sourceFile,nodeInstanceId,operation,title,fileName,targetLeafAddress,sortOrder\r\n')}>下载清单模板</button>
      <label>导入清单 <input aria-label="导入装配清单" type="file" accept=".csv,.xlsx" disabled={!editable}
        onChange={event => { void loadFile(event.target.files?.[0]) }} /></label></div>
    <textarea aria-label="装配清单 CSV" rows={5} value={manifest} disabled={disabled || !!handle || fileBusy}
      className="w-full rounded border p-2" onChange={event => { setManifest(event.target.value); setWorkbook(undefined); invalidate() }}
      placeholder={'sourceFile,nodeInstanceId,operation,title,fileName,sortOrder\ndocument:<id>,<node-id>,new,Specification,specification.pdf,0'} />
    {workbook && <p>已选择 XLSX：{workbook.name}。最多一个工作表、32 列、1000 行；公式不执行，宏和外部链接会被拒绝。</p>}
    <div className="flex gap-3">
      <button type="button" disabled={!editable || workspaceRevision === undefined || (!workbook && !manifest.trim())} onClick={() => { void generate() }}>生成装配预览</button>
      <button type="button" disabled={!canExecute} onClick={() => { void execute() }}>执行已预览计划</button>
    </div>
    {preview && <div className="space-y-2">
      <p>总行数 {preview.plan.totalRows} · 有效 {preview.plan.validRows} · 无效 {preview.plan.invalidRows} · 已选 {selected.length} 行</p>
      <p>计划摘要 {preview.plan.planDigest.slice(0, 12)}…{selectionDirty && '；行选择已变化，请重新预览所选有效行。'}</p>
      {preview.plan.diagnostics.map((issue, index) => <p role="alert" key={index}>{issue.message}</p>)}
      <div className="flex gap-3"><button type="button" disabled={!editable || selected.length === 0} onClick={() => { void selectSubset() }}>预览所选有效行</button>
        <button type="button" disabled={!editable} onClick={() => setSelected(preview.plan.rows.filter(row => row.status === 0).map(row => row.lineNumber))}>选择全部有效行</button>
        <button type="button" onClick={exportIssues}>导出问题清单</button></div>
      <div className="overflow-auto"><table><thead><tr><th>选择</th><th>行 / 状态</th><th>来源 / 目标文件</th><th>业务节点 / 历史目标</th><th>诊断</th></tr></thead>
        <tbody>{preview.plan.rows.slice(page * 10, page * 10 + 10).map(row => <tr key={row.lineNumber}>
          <td><input type="checkbox" aria-label={`选择装配行 ${row.lineNumber}`} disabled={!editable || row.status !== 0} checked={selected.includes(row.lineNumber)}
            onChange={event => { setSelected(event.target.checked ? [...selected, row.lineNumber] : selected.filter(line => line !== row.lineNumber)); setError(undefined) }} /></td>
          <td>{row.lineNumber} · {row.status === 0 ? '有效' : '无效'} · {row.operation}</td>
          <td>{row.sourceFile || row.documentId || '生命周期删除，无新文件'}<br />{row.fileName || '保留文件名'} → {row.targetHref || '尚未解析'}</td>
          <td>{row.nodeInstanceId || '无节点'}<br />{row.targetLeafAddress || '无历史目标'}</td>
          <td>{row.diagnostics.map(issue => issue.message).join('；') || '—'}</td>
        </tr>)}</tbody></table></div>
      <div className="flex gap-3"><button type="button" disabled={page === 0} onClick={() => setPage(page - 1)}>上一页装配行</button>
        <span>第 {page + 1} 页</span><button type="button" disabled={(page + 1) * 10 >= preview.plan.rows.length} onClick={() => setPage(page + 1)}>下一页装配行</button></div>
    </div>}
    {handle && <div className="space-y-2"><p>已保存装配请求。{handle.expectedRevision !== workspaceRevision && '工作区修订已变化，仍可查询原任务。'}</p>
      {handle.jobId ? <button type="button" disabled={busy} onClick={() => { void refresh() }}>刷新装配状态</button>
        : <button type="button" disabled={busy} onClick={() => { void recover() }}>恢复装配任务</button>}
      {job && <><p role="status">{states[job.status]} · 尝试 {job.attemptCount}</p>
        {job.failureMessage && <p role="alert">{job.failureMessage}</p>}
        {job.result && <p>工作区修订已更新为 {job.result.workspaceRevision}，创建 {job.result.documents.length} 个挂载。</p>}
        <div className="flex gap-3"><button type="button" disabled={disabled || busy || !running || job.cancellationRequested} onClick={() => { void refresh(true) }}>取消装配任务</button>
          <button type="button" disabled={busy || running} onClick={reset}>创建新装配计划</button></div></>}
    </div>}
  </section>
}
