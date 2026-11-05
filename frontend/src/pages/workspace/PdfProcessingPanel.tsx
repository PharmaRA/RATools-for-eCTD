import { useEffect, useRef, useState } from 'react'
import { ApiRequestError, apiFetch, apiFetchResponse, buildJsonRequestInit } from '../../apiClient'
import type { PdfProcessingAdoption, PdfProcessingParameters, PdfProcessingPlan, ProcessingInspection, ProcessingJobView } from '../../api/generated'
import { PdfBookmarksEditor, type BookmarkEdit } from './PdfBookmarksEditor'
import { PdfLinksEditor, type LinkEdit, type RemotePdfTarget } from './PdfLinksEditor'

type Props = {
  appId: string
  seqNumber: string
  placementId: string
  documentId: string
  workspaceRevision?: number
  disabled: boolean
  remoteTargets: readonly RemotePdfTarget[]
  onAdopted: () => void | Promise<void>
}
export function PdfProcessingPanel(props: Props) {
  return <ProcessingEditor key={JSON.stringify([props.appId, props.seqNumber, props.placementId,
    props.documentId, props.workspaceRevision, props.disabled])} {...props} />
}

type ExecutionHandle = { planId: string; planDigest: string; idempotencyKey: string;
  expectedRevision: number; sourceDocumentId: string; jobId?: string }
const sameRequest = (left: ExecutionHandle | undefined, right: ExecutionHandle) => !!left
  && left.planId === right.planId && left.planDigest === right.planDigest && left.idempotencyKey === right.idempotencyKey
function readHandle(key: string): ExecutionHandle | undefined {
  try {
    const saved: unknown = JSON.parse(localStorage.getItem(key) ?? 'null')
    if (!saved || typeof saved !== 'object') return undefined
    const value = saved as Partial<ExecutionHandle>
    return typeof value.planId === 'string' && typeof value.planDigest === 'string'
      && typeof value.idempotencyKey === 'string' && typeof value.sourceDocumentId === 'string'
      && Number.isSafeInteger(value.expectedRevision) && (value.jobId === undefined || typeof value.jobId === 'string')
      ? value as ExecutionHandle : undefined
  } catch { return undefined }
}

const statusText = ['排队中', '处理中', '处理完成，等待采用', '处理失败', '已取消', '处理超时']
const modeOptions = [['UseNone', '仅显示页面'], ['UseOutlines', '显示书签'], ['UseThumbs', '显示缩略图'],
  ['FullScreen', '全屏'], ['UseOC', '显示图层'], ['UseAttachments', '显示附件']]
const message = (error: unknown) => error instanceof Error ? error.message : 'PDF 处理操作失败'

function ProcessingEditor({ appId, seqNumber, placementId, documentId, workspaceRevision, disabled,
  remoteTargets, onAdopted }: Props) {
  const base = `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seqNumber)}/pdf-processing`
  const handleKey = `ratools.pdf-processing:${JSON.stringify([appId, seqNumber, placementId, documentId])}`
  const [handle, setHandle] = useState(() => readHandle(handleKey))
  const [inspection, setInspection] = useState<ProcessingInspection>()
  const [pageMode, setPageMode] = useState<string | null>(null)
  const [bookmarks, setBookmarks] = useState<readonly BookmarkEdit[] | null>(null)
  const [links, setLinks] = useState<readonly LinkEdit[]>([])
  const [bookmarksValid, setBookmarksValid] = useState(true)
  const [linksValid, setLinksValid] = useState(true)
  const [plan, setPlan] = useState<PdfProcessingPlan>()
  const [job, setJob] = useState<ProcessingJobView>()
  const [adopted, setAdopted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState<string>()
  const [pollError, setPollError] = useState<string>()
  const [error, setError] = useState<string>()
  const active = useRef(true)
  const inputGeneration = useRef(0)
  const jobGeneration = useRef(0)
  const enqueueKey = useRef<string | undefined>(undefined)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const running = job?.status === 0 || job?.status === 1
  const navigation = inspection?.navigation
  const editable = !disabled && !busy && !job && !handle && !!navigation && !navigation.isEncrypted && !navigation.hasSignature
    && navigation.navigationComplete && navigation.destinationsComplete && (navigation.pageCount ?? 0) > 0
  const hasChanges = pageMode !== null || bookmarks !== null || links.length > 0
  const canPreview = editable && bookmarksValid && linksValid && hasChanges
  const canExecute = !disabled && !busy && !job && (!!handle || (editable && !!plan && bookmarksValid && linksValid))
  const originalDraft = handle?.expectedRevision === workspaceRevision && handle?.sourceDocumentId === documentId
  const incompleteResult = job?.status === 2 && (!job.resultVersionId || !job.resultSha256)
  const canAdopt = !disabled && !busy && originalDraft && job?.status === 2 && !incompleteResult
    && !!job.resultVersionId && !!job.resultSha256 && !adopted && !job.adoption
  const saveHandle = (value: ExecutionHandle) => {
    localStorage.setItem(handleKey, JSON.stringify(value))
    if (active.current) setHandle(value)
  }
  const abandonHandle = () => {
    localStorage.setItem(handleKey, 'null')
    setHandle(undefined)
    setJob(undefined)
    setPlan(undefined)
    enqueueKey.current = undefined
    setError(undefined)
  }

  useEffect(() => {
    if (!job || !running) return
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>
    const poll = async () => {
      const generation = jobGeneration.current
      try {
        const result: ProcessingJobView = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}`)
        if (cancelled || !active.current || generation !== jobGeneration.current) return
        if (result.id !== job.id || result.planId !== job.planId) throw new Error('处理任务身份不匹配，请重新读取状态。')
        setJob(result); setPollError(undefined)
      } catch (caught) { if (!cancelled && active.current && generation === jobGeneration.current) setPollError(message(caught)) }
      if (!cancelled) timer = setTimeout(() => { void poll() }, 1500)
    }
    timer = setTimeout(() => { void poll() }, 1500)
    return () => { cancelled = true; clearTimeout(timer) }
  }, [base, job, running])

  const invalidate = () => { inputGeneration.current++; setPlan(undefined); enqueueKey.current = undefined; setError(undefined) }
  const inspect = async () => {
    if (disabled || busy || running || workspaceRevision === undefined) return
    localStorage.setItem(handleKey, 'null'); setHandle(undefined)
    setBusy(true); invalidate(); setInspection(undefined); setJob(undefined); setAdopted(false)
    setPageMode(null); setBookmarks(null); setLinks([])
    try {
      const result: ProcessingInspection = await apiFetch(`${base}/placements/${encodeURIComponent(placementId)}/inspection`,
        buildJsonRequestInit('POST', { expectedRevision: workspaceRevision }))
      if (!active.current) return
      if (result.workspaceRevision !== workspaceRevision || result.sourceDocumentId !== documentId)
        throw new Error('文档或工作区修订已变化，请刷新工作区。')
      setInspection(result)
    } catch (caught) { if (active.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const preview = async () => {
    if (!canPreview || !inspection) return
    const generation = ++inputGeneration.current
    setBusy(true); setError(undefined); setPlan(undefined); enqueueKey.current = undefined
    const parameters: PdfProcessingParameters = { pageMode, bookmarks: bookmarks === null ? null : [...bookmarks], links: [...links], referenceEdits: null }
    try {
      const result: PdfProcessingPlan = await apiFetch(`${base}/placements/${encodeURIComponent(placementId)}/plans`,
        buildJsonRequestInit('POST', { sourceVersionId: inspection.sourceVersionId, expectedRevision: workspaceRevision, parameters }))
      if (!active.current || generation !== inputGeneration.current) return
      if (result.applicationId !== appId || result.sequenceNumber !== seqNumber || result.placementId !== placementId
        || result.sourceVersionId !== inspection.sourceVersionId || result.sourceDocumentId !== documentId
        || result.expectedWorkspaceRevision !== workspaceRevision || result.sourceSha256 !== inspection.sha256
        || result.sourceMd5 !== inspection.md5 || result.engineVersion !== navigation?.engineVersion || !result.planDigest)
        throw new Error('处理预览与当前源版本不一致，请重新读取 PDF 导航。')
      setPlan(result); enqueueKey.current = crypto.randomUUID()
    } catch (caught) { if (active.current && generation === inputGeneration.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const execute = async () => {
    if (!canExecute || (!handle && (!plan || !enqueueKey.current))) return
    setBusy(true); setError(undefined)
    const request = handle ?? { planId: plan!.id, planDigest: plan!.planDigest, idempotencyKey: enqueueKey.current!,
      expectedRevision: plan!.expectedWorkspaceRevision, sourceDocumentId: plan!.sourceDocumentId }
    try {
      const saved = readHandle(handleKey)
      if (saved && !sameRequest(saved, request)) throw new Error('此文档已有较新的处理请求，请重新打开面板。')
      saveHandle(request)
      const result: ProcessingJobView = await apiFetch(`${base}/jobs`, buildJsonRequestInit('POST', {
        planId: request.planId, planDigest: request.planDigest, idempotencyKey: request.idempotencyKey,
      }))
      if (result.planId !== request.planId) throw new Error('返回任务与已确认计划不一致。')
      if (!sameRequest(readHandle(handleKey), request)) return
      saveHandle({ ...request, jobId: result.id })
      if (active.current) {
        setJob(result)
      }
    } catch (caught) {
      if (!handle && caught instanceof ApiRequestError && [400, 404, 409].includes(caught.status)
        && sameRequest(readHandle(handleKey), request)) {
        localStorage.setItem(handleKey, 'null')
        if (active.current) { setHandle(undefined); setPlan(undefined); enqueueKey.current = undefined }
      }
      if (active.current) setError(message(caught))
    }
    finally { if (active.current) setBusy(false) }
  }
  const refresh = async () => {
    if (busy || !handle?.jobId) return
    setBusy(true); setPollError(undefined); jobGeneration.current++
    try {
      const result: ProcessingJobView = await apiFetch(`${base}/jobs/${encodeURIComponent(handle.jobId)}`)
      if (result.id !== handle.jobId || result.planId !== handle.planId) throw new Error('处理任务身份不匹配。')
      if (active.current) setJob(result)
    } catch (caught) { if (active.current) setPollError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const cancel = async () => {
    if (disabled || busy || !running || !job) return
    setBusy(true); setError(undefined); jobGeneration.current++
    try {
      const result: ProcessingJobView = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}/cancel`, { method: 'POST' })
      if (active.current) setJob(result)
    } catch (caught) { if (active.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const adopt = async () => {
    if (!canAdopt || !job || !handle) return
    setBusy(true); setError(undefined)
    try {
      const result: PdfProcessingAdoption = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}/adopt`,
        buildJsonRequestInit('POST', { expectedRevision: handle.expectedRevision }))
      if (active.current) { setAdopted(true); setJob({ ...job, adoption: result }) }
      await onAdopted()
    } catch (caught) { if (active.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const download = async (versionId: string, sha256: string, fileName: string) => {
    if (downloading) return
    setDownloading(true); setDownloadError(undefined)
    try {
      const response = await apiFetchResponse(`${base}/versions/${encodeURIComponent(versionId)}/content`)
      const blob = await response.blob()
      const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await blob.arrayBuffer())))
        .map(byte => byte.toString(16).padStart(2, '0')).join('')
      if (hash !== sha256.toLowerCase()) throw new Error('下载内容摘要与所选版本不一致。')
      if (!active.current) return
      const url = URL.createObjectURL(blob)
      try { const anchor = document.createElement('a'); anchor.href = url; anchor.download = fileName; anchor.click() }
      finally { setTimeout(() => URL.revokeObjectURL(url), 1000) }
    } catch (caught) { if (active.current) setDownloadError(message(caught)) }
    finally { if (active.current) setDownloading(false) }
  }
  return <section aria-label="PDF 导航处理" className="rounded border bg-white p-4 space-y-3">
    <h3 className="font-medium">PDF 书签与链接处理</h3>
    <p>读取当前内容版本，预览导航变化后执行。处理结果经过检查后，由你选择是否采用到草稿。</p>
    {error && <p role="alert">{error}</p>}
    {downloadError && <p role="alert">{downloadError}</p>}
    {pollError && <p role="alert">{pollError}</p>}
    {handle && <div className="space-y-2">
      <p>已保存处理请求，可继续查询或恢复同一任务。</p>
      {!originalDraft && <p role="alert">草稿修订已变化或已选择不同文档；此任务可以查询、下载和取消，不能采用到当前草稿。</p>}
      {handle.jobId ? <button type="button" disabled={busy} onClick={() => { void refresh() }}>刷新处理状态</button>
        : <>
          <button type="button" disabled={!canExecute} onClick={() => { void execute() }}>恢复处理任务</button>
          <button type="button" disabled={busy} onClick={abandonHandle}>放弃本地处理请求</button>
        </>}
    </div>}
    <button type="button" disabled={disabled || busy || running || workspaceRevision === undefined} onClick={() => { void inspect() }}>
      {handle && !job ? '重新检查 PDF 导航' : '读取 PDF 导航'}
    </button>
    {busy && <p role="status">正在处理请求…</p>}
    {inspection && navigation && <>
      <p>页数 {navigation.pageCount ?? '未知'} · 当前初始视图 {navigation.pageMode ?? '默认'} · 源摘要 {inspection.sha256.slice(0, 12)}…</p>
      <button type="button" disabled={downloading} onClick={() => { void download(inspection.sourceVersionId, inspection.sha256, 'source.pdf') }}>下载源版本</button>
      {(navigation.isEncrypted || navigation.hasSignature || !navigation.navigationComplete || !navigation.destinationsComplete) && <p role="alert">
        {navigation.isEncrypted && '加密 PDF 暂不能处理。'}{navigation.hasSignature && '文件包含数字签名，当前导航编辑会影响签名，已阻止处理。'}
        {(!navigation.navigationComplete || !navigation.destinationsComplete) && '导航或命名目的地检查未完成，暂不能处理。'}
        {navigation.incompleteReasons.join('；')}
      </p>}
      <label>初始视图 <select aria-label="PDF 初始视图" disabled={!editable} value={pageMode ?? ''}
        onChange={event => { setPageMode(event.target.value || null); invalidate() }}>
        <option value="">保留原初始视图</option>{modeOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select></label>
      <PdfBookmarksEditor original={navigation.bookmarks.map(item => ({ title: item.title, level: item.level,
        namedDestination: item.namedDestination, pageNumber: item.namedDestination ? null : item.pageNumber }))}
        value={bookmarks} pageCount={navigation.pageCount ?? 0} disabled={!editable}
        namedDestinations={navigation.namedDestinations.filter(item => item.isValid).map(item => item.name)}
        onChange={value => { setBookmarks(value); invalidate() }} onValidityChange={setBookmarksValid} />
      <PdfLinksEditor original={navigation.links} value={links} pageCount={navigation.pageCount ?? 0} disabled={!editable}
        namedDestinations={navigation.namedDestinations.filter(item => item.isValid).map(item => item.name)} remoteTargets={remoteTargets}
        onChange={value => { setLinks(value); invalidate() }} onValidityChange={setLinksValid} />
      <div className="flex gap-3">
        <button type="button" disabled={!canPreview} onClick={() => { void preview() }}>生成处理预览</button>
        <button type="button" disabled={!canExecute} onClick={() => { void execute() }}>执行已预览处理</button>
      </div>
    </>}
    {plan && <div className="space-y-2">
      <p>计划摘要 {plan.planDigest.slice(0, 12)}… · 草稿修订 {plan.expectedWorkspaceRevision}</p>
      <p>初始视图：{pageMode ?? '保留原值'}；书签：{bookmarks === null ? '保留原值' : `替换为 ${bookmarks.length} 个`}；修改链接：{links.length} 个。</p>
    </div>}
    {job && <div className="space-y-2" aria-label="处理任务">
      <p role="status">{incompleteResult ? '处理完成但结果不完整' : adopted || job.adoption ? '处理结果已采用到草稿' : statusText[job.status]} · 尝试 {job.attemptCount}</p>
      {incompleteResult && <p role="alert">处理完成但结果缺少版本或 SHA-256 摘要，无法下载或采用。</p>}
      {job.failureMessage && <p role="alert">{job.failureMessage}</p>}
      {job.cancellationRequested && running && <p>已请求取消，正在等待任务结束。</p>}
      {job.resultVersionId && job.resultSha256 && <button type="button" disabled={downloading}
        onClick={() => { void download(job.resultVersionId!, job.resultSha256!, 'processed.pdf') }}>下载处理结果</button>}
      <div className="flex gap-3">
        <button type="button" disabled={disabled || busy || !running || job.cancellationRequested} onClick={() => { void cancel() }}>取消处理</button>
        <button type="button" disabled={!canAdopt} onClick={() => { void adopt() }}>采用处理结果</button>
      </div>
    </div>}
  </section>
}
