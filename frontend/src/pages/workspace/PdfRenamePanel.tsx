import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ApiRequestError, apiFetch, apiFetchResponse, buildJsonRequestInit } from '../../apiClient'
import type { PdfRelocationChange, PdfRenameAdoption, PdfRenameJobView, PdfRenamePreview } from '../../api/generated'

export type RenameCandidate = { placementId: string; fileName: string; href: string; nodeInstanceId: string | null }
export type RenameNode = { id: string; label: string }
type Props = { appId: string; seqNumber: string; workspaceRevision?: number; disabled: boolean;
  candidates: readonly RenameCandidate[]; nodes: readonly RenameNode[]; onAdopted: () => void | Promise<void> }

type Handle = { planId: string; planDigest: string; idempotencyKey: string; expectedRevision: number; jobId?: string }
const same = (left: Handle | undefined, right: Handle) => !!left && left.planId === right.planId
  && left.planDigest === right.planDigest && left.idempotencyKey === right.idempotencyKey
function read(key: string): Handle | undefined {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(key) ?? 'null')
    if (!value || typeof value !== 'object') return undefined
    const item = value as Partial<Handle>
    return typeof item.planId === 'string' && typeof item.planDigest === 'string' && typeof item.idempotencyKey === 'string'
      && Number.isSafeInteger(item.expectedRevision) && (item.jobId === undefined || typeof item.jobId === 'string') ? item as Handle : undefined
  } catch { return undefined }
}
const errorMessage = (error: unknown) => error instanceof Error ? error.message : '批量改名操作失败'
const states = ['排队中', '处理中', '全部结果已检查，等待采用', '处理失败', '已取消', '处理超时']
export function PdfRenamePanel(props: Props) {
  return <RenameEditor key={JSON.stringify([props.appId, props.seqNumber, props.workspaceRevision,
    props.candidates.map(item => [item.placementId, item.href, item.nodeInstanceId])])} {...props} />
}

function RenameEditor({ appId, seqNumber, workspaceRevision, disabled, candidates, nodes, onAdopted }: Props) {
  const base = `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seqNumber)}/pdf-renames`
  const storageKey = `ratools.pdf-renames:${JSON.stringify([appId, seqNumber])}`
  const [changes, setChanges] = useState<PdfRelocationChange[]>([])
  const [confirmed, setConfirmed] = useState<string[]>([])
  const [preview, setPreview] = useState<PdfRenamePreview>()
  const [dirty, setDirty] = useState(true)
  const [handle, setHandle] = useState(() => read(storageKey))
  const [job, setJob] = useState<PdfRenameJobView>()
  const [adopted, setAdopted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [error, setError] = useState<string>()
  const [downloadError, setDownloadError] = useState<string>()
  const [pollError, setPollError] = useState<string>()
  const [page, setPage] = useState(0)
  const alive = useRef(true)
  const readOnly = useRef(disabled)
  useLayoutEffect(() => { readOnly.current = disabled }, [disabled])
  const generation = useRef(0)
  const commandGeneration = useRef(0)
  const key = useRef<string | undefined>(undefined)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const running = job?.status === 0 || job?.status === 1
  const canEdit = !disabled && !busy && !handle
  const canExecute = !disabled && !busy && !job && (!!handle || (!!preview?.plan.canExecute && !dirty))
  const label = (id: string) => {
    const candidate = candidates.find(item => item.placementId === id)
    if (candidate) return `${seqNumber}/${candidate.href}`
    const source = preview?.sources.find(item => item.placementId === id)
    return source ? `${source.sequenceNumber}/${source.href}` : id
  }
  const persist = (value: Handle) => { localStorage.setItem(storageKey, JSON.stringify(value)); if (alive.current) setHandle(value) }
  const invalidate = () => { generation.current++; setDirty(true); key.current = undefined; setError(undefined) }

  useEffect(() => {
    if (!running || !job) return
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>
    const poll = async () => {
      const current = commandGeneration.current
      try {
        const result: PdfRenameJobView = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}`)
        if (!cancelled && alive.current && current === commandGeneration.current) {
          if (result.id !== job.id || result.planId !== job.planId) throw new Error('改名任务身份不匹配。')
          setJob(result); setPollError(undefined)
        }
      } catch (caught) { if (!cancelled && alive.current) setPollError(errorMessage(caught)) }
      if (!cancelled) timer = setTimeout(() => { void poll() }, 1500)
    }
    timer = setTimeout(() => { void poll() }, 1500)
    return () => { cancelled = true; clearTimeout(timer) }
  }, [base, job, running])

  const generate = async () => {
    if (!canEdit || changes.length === 0 || workspaceRevision === undefined) return
    setBusy(true); setError(undefined); setDirty(true)
    const current = ++generation.current
    try {
      const result: PdfRenamePreview = await apiFetch(`${base}/plans`, buildJsonRequestInit('POST', {
        expectedRevision: workspaceRevision, changes,
        confirmedSources: [...new Set([...changes.map(item => item.placementId), ...confirmed])],
      }))
      if (!alive.current || current !== generation.current) return
      if (result.plan.applicationId !== appId || result.plan.sequenceNumber !== seqNumber
        || result.plan.expectedRevision !== workspaceRevision || !result.plan.planDigest)
        throw new Error('改名预览不属于当前工作区修订，请重新预览。')
      setPreview(result); setDirty(false); key.current = crypto.randomUUID()
    } catch (caught) { if (alive.current && current === generation.current) setError(errorMessage(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const execute = async () => {
    if (!canExecute || (!handle && (!preview || !key.current))) return
    const request = handle ?? { planId: preview!.id, planDigest: preview!.plan.planDigest,
      idempotencyKey: key.current!, expectedRevision: preview!.plan.expectedRevision }
    setBusy(true); setError(undefined)
    try {
      const saved = read(storageKey)
      if (saved && !same(saved, request)) throw new Error('已有较新的改名任务，请重新打开工作区。')
      persist(request)
      const result: PdfRenameJobView = await apiFetch(`${base}/jobs`, buildJsonRequestInit('POST', {
        planId: request.planId, planDigest: request.planDigest, idempotencyKey: request.idempotencyKey,
      }))
      if (result.planId !== request.planId) throw new Error('返回任务与已确认改名计划不一致。')
      if (!same(read(storageKey), request)) return
      persist({ ...request, jobId: result.id }); if (alive.current) setJob(result)
    } catch (caught) {
      if (!handle && caught instanceof ApiRequestError && [400, 404, 409].includes(caught.status) && same(read(storageKey), request)) {
        localStorage.setItem(storageKey, 'null'); if (alive.current) { setHandle(undefined); invalidate() }
      }
      if (alive.current) setError(errorMessage(caught))
    } finally { if (alive.current) setBusy(false) }
  }
  const jobCommand = async (cancel: boolean) => {
    if (busy || !handle?.jobId || (cancel && (disabled || !running))) return
    setBusy(true); setError(undefined); commandGeneration.current++
    try {
      const result: PdfRenameJobView = await apiFetch(`${base}/jobs/${encodeURIComponent(handle.jobId)}${cancel ? '/cancel' : ''}`,
        cancel ? { method: 'POST' } : undefined)
      if (result.id !== handle.jobId || result.planId !== handle.planId) throw new Error('返回任务身份不匹配。')
      if (alive.current) setJob(result)
    } catch (caught) { if (alive.current) setError(errorMessage(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const recover = async () => {
    if (!handle || busy) return
    setBusy(true); setError(undefined)
    try {
      const existing: PdfRenameJobView = await apiFetch(`${base}/plans/${encodeURIComponent(handle.planId)}/job`)
      if (existing.planId !== handle.planId) throw new Error('恢复任务与改名计划不一致。')
      if (!same(read(storageKey), handle)) return
      persist({ ...handle, jobId: existing.id }); if (alive.current) setJob(existing)
    } catch (caught) {
      if (caught instanceof ApiRequestError && caught.status === 404) {
        if (!same(read(storageKey), handle)) return
        if (!alive.current || readOnly.current) {
          if (alive.current) setError('当前工作区为只读状态；未找到已创建任务，不能重新入队。')
          return
        }
        try {
          const result: PdfRenameJobView = await apiFetch(`${base}/jobs`, buildJsonRequestInit('POST', {
            planId: handle.planId, planDigest: handle.planDigest, idempotencyKey: handle.idempotencyKey,
          }))
          if (result.planId !== handle.planId) throw new Error('恢复任务与改名计划不一致。')
          if (!same(read(storageKey), handle)) return
          persist({ ...handle, jobId: result.id }); if (alive.current) setJob(result)
        } catch (retryError) {
          if (retryError instanceof ApiRequestError && [400, 404, 409].includes(retryError.status) && same(read(storageKey), handle)) {
            localStorage.setItem(storageKey, 'null')
            if (alive.current) { setHandle(undefined); setPreview(undefined); setDirty(true); key.current = undefined }
          }
          if (alive.current) setError(errorMessage(retryError))
        }
      } else if (alive.current) setError(errorMessage(caught))
    } finally { if (alive.current) setBusy(false) }
  }
  const adopt = async () => {
    if (disabled || busy || !job || job.status !== 2 || !handle || handle.expectedRevision !== workspaceRevision || adopted || job.adoption) return
    setBusy(true); setError(undefined)
    try {
      const result: PdfRenameAdoption = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}/adopt`,
        buildJsonRequestInit('POST', { expectedRevision: handle.expectedRevision }))
      if (alive.current) { setAdopted(true); setJob({ ...job, adoption: result }) }
      await onAdopted()
    } catch (caught) { if (alive.current) setError(errorMessage(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const download = async (placementId: string, expectedHash: string, href: string) => {
    if (downloading || !job) return
    setDownloading(true); setDownloadError(undefined)
    try {
      const response = await apiFetchResponse(`${base}/jobs/${encodeURIComponent(job.id)}/placements/${encodeURIComponent(placementId)}/content`)
      const data = await response.blob()
      const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await data.arrayBuffer())))
        .map(byte => byte.toString(16).padStart(2, '0')).join('')
      if (hash !== expectedHash.toLowerCase()) throw new Error('改名结果下载摘要不匹配。')
      if (!alive.current) return
      const url = URL.createObjectURL(data)
      try { const anchor = document.createElement('a'); anchor.href = url; anchor.download = href.split('/').at(-1)!; anchor.click() }
      finally { setTimeout(() => URL.revokeObjectURL(url), 1000) }
    } catch (caught) { if (alive.current) setDownloadError(errorMessage(caught)) }
    finally { if (alive.current) setDownloading(false) }
  }
  const reset = () => {
    if (busy || running || (handle && !job)) return
    if (handle) {
      const current = read(storageKey)
      if (!same(current, handle)) { if (alive.current) setError('发现较新的改名任务，请先刷新状态。'); return }
    }
    localStorage.setItem(storageKey, 'null'); setHandle(undefined); setJob(undefined); setPreview(undefined); setAdopted(false); setConfirmed([]); invalidate()
  }
  return <section aria-label="批量改名与移动" className="rounded border bg-white p-4 space-y-3 mb-3">
    <h3 className="font-medium">批量改名与移动（PDF）</h3>
    <p>先选择文件和目标业务节点，再预览全部链接影响。引用源必须明确确认，整批结果检查通过后一次采用。</p>
    {error && <p role="alert">{error}</p>}{pollError && <p role="alert">{pollError}</p>}{downloadError && <p role="alert">{downloadError}</p>}
    {handle && <div>
      <p>已保存改名任务请求。{handle.expectedRevision !== workspaceRevision && '草稿修订已变化，仍可查看或取消任务，不能采用旧结果。'}</p>
      {handle.jobId ? <button type="button" disabled={busy} onClick={() => { void jobCommand(false) }}>刷新改名状态</button>
        : <button type="button" disabled={busy} onClick={() => { void recover() }}>恢复改名任务</button>}
    </div>}
    <fieldset disabled={!canEdit} className="space-y-2">
      <legend>选择要改名或移动的文件</legend>
      {candidates.slice(page * 20, page * 20 + 20).map(candidate => {
        const change = changes.find(item => item.placementId === candidate.placementId)
        const update = (values: Partial<PdfRelocationChange>) => { setChanges(changes.map(item => item === change ? { ...item, ...values } : item)); invalidate() }
        return <div key={candidate.placementId} className="rounded border p-2 flex flex-wrap gap-3">
          <label><input type="checkbox" aria-label={`选择改名 ${candidate.fileName}`} checked={!!change}
            onChange={event => { setChanges(event.target.checked ? [...changes, { placementId: candidate.placementId, fileName: candidate.fileName, targetNodeInstanceId: null }]
              : changes.filter(item => item.placementId !== candidate.placementId)); invalidate() }} /> {candidate.fileName} · {candidate.href}</label>
          {change && <>
            <label>新文件名 <input aria-label={`新文件名 ${candidate.fileName}`} value={change.fileName}
              onChange={event => update({ fileName: event.target.value })} /></label>
            <label>目标业务节点 <select aria-label={`目标节点 ${candidate.fileName}`} value={change.targetNodeInstanceId ?? ''}
              onChange={event => update({ targetNodeInstanceId: event.target.value || null })}>
              <option value="">保留当前目录和业务节点</option>{nodes.map(node => <option key={node.id} value={node.id}>{node.label}</option>)}
            </select></label>
          </>}
        </div>
      })}
      <div className="flex gap-3"><button type="button" disabled={page === 0} onClick={() => setPage(page - 1)}>上一页文件</button>
        <span>第 {page + 1} 页，共 {candidates.length} 份文件</span>
        <button type="button" disabled={(page + 1) * 20 >= candidates.length} onClick={() => setPage(page + 1)}>下一页文件</button></div>
    </fieldset>
    {preview && <div className="space-y-2">
      <p>受影响来源 {preview.plan.requiredSources.length} 份，需改写链接 {preview.plan.impacts.length} 条。{dirty && '选项已变化，请重新预览。'}</p>
      {preview.plan.changes.map(change => <p key={change.placementId}>{label(change.placementId)} → {change.newHref}</p>)}
      <ul>{preview.plan.impacts.map(impact => <li key={`${impact.sourcePlacementId}:${impact.objectPath}`}>
        {label(impact.sourcePlacementId)} · 第 {impact.sourcePageNumber ?? '未知'} 页：{impact.oldReference} → {impact.newReference}
      </li>)}</ul>
      <fieldset disabled={!canEdit}><legend>确认引用源同时处理</legend>
        {preview.plan.requiredSources.filter(id => !changes.some(change => change.placementId === id)).map(id => <label key={id} className="block">
          <input type="checkbox" aria-label={`确认引用源 ${label(id)}`} checked={confirmed.includes(id)} onChange={event => {
            setConfirmed(event.target.checked ? [...confirmed, id] : confirmed.filter(item => item !== id)); invalidate()
          }} /> 同时处理 {label(id)}</label>)}
      </fieldset>
      {preview.plan.issues.length > 0 && <ul role="alert">{preview.plan.issues.map((issue, index) => <li key={index}>
        {issue.placementId && `${label(issue.placementId)}：`}{issue.message}</li>)}</ul>}
      {!dirty && <p>计划摘要 {preview.plan.planDigest.slice(0, 12)}…</p>}
    </div>}
    <div className="flex gap-3">
      <button type="button" disabled={!canEdit || changes.length === 0 || workspaceRevision === undefined} onClick={() => { void generate() }}>预览改名影响</button>
      <button type="button" disabled={!canExecute} onClick={() => { void execute() }}>执行已确认改名</button>
    </div>
    {job && <div className="space-y-2">
      <p role="status">{job.adoption || adopted ? '整批改名结果已采用' : states[job.status]} · 尝试 {job.attemptCount}</p>
      {job.failureMessage && <p role="alert">{job.failureMessage}</p>}
      {job.results.map(output => <p key={output.placementId}>{output.href} <button type="button" disabled={downloading}
        onClick={() => { void download(output.placementId, output.sha256, output.href) }}>下载 {label(output.placementId)}</button></p>)}
      <div className="flex gap-3">
        <button type="button" disabled={disabled || busy || !running || job.cancellationRequested} onClick={() => { void jobCommand(true) }}>取消改名处理</button>
        <button type="button" disabled={disabled || busy || job.status !== 2 || handle?.expectedRevision !== workspaceRevision || !!job.adoption || adopted}
          onClick={() => { void adopt() }}>采用全部改名结果</button>
        <button type="button" disabled={busy || running} onClick={reset}>创建新的改名计划</button>
      </div>
    </div>}
  </section>
}
