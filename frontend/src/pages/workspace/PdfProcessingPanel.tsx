import { useEffect, useRef, useState } from 'react'
import { ApiRequestError, apiFetch, apiFetchResponse, buildJsonRequestInit } from '../../apiClient'
import type { PdfContentEvidence, PdfContentInspection, PdfProcessingAdoption, PdfProcessingParameters, PdfProcessingPlan, ProcessingInspection, ProcessingJobView } from '../../api/generated'
import { PdfBookmarksEditor, type BookmarkEdit } from './PdfBookmarksEditor'
import { PdfLinksEditor, type LinkEdit, type RemotePdfTarget } from './PdfLinksEditor'
import { PdfContentEditor, type ContentOptions } from './PdfContentEditor'

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
  expectedRevision: number; sourceDocumentId: string; jobId?: string;
  content?: { engineBinding: string; sourceSha256: string; parameters: ContentOptions } }
function contentSignature(value: unknown): string | undefined {
  if (!value || typeof value !== 'object') return undefined
  const options = value as Partial<ContentOptions>
  if (typeof options.embedStandardFonts !== 'boolean' || (options.targetVersion !== null && options.targetVersion !== '1.7')) return undefined
  if (options.ocr !== null && (typeof options.ocr !== 'object' || !options.ocr || options.ocr.language !== 'eng' || options.ocr.dpi !== 150
    || !Array.isArray(options.ocr.pages) || options.ocr.pages.length < 1 || options.ocr.pages.length > 500
    || options.ocr.pages.some((page, index, pages) => !Number.isSafeInteger(page) || page < 1 || page > 500 || (index > 0 && page <= pages[index - 1])))) return undefined
  if (!options.ocr && !options.embedStandardFonts && !options.targetVersion) return undefined
  return JSON.stringify({ ocr: options.ocr ? { language: options.ocr.language, pages: options.ocr.pages, dpi: options.ocr.dpi } : null,
    embedStandardFonts: options.embedStandardFonts, targetVersion: options.targetVersion })
}
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
      && (value.content === undefined || (value.content !== null && typeof value.content === 'object'
        && typeof value.content.engineBinding === 'string' && typeof value.content.sourceSha256 === 'string'
        && contentSignature(value.content.parameters) !== undefined))
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
  const [operationMode, setOperationMode] = useState<'navigation' | 'content'>('navigation')
  const [contentInspection, setContentInspection] = useState<PdfContentInspection>()
  const [content, setContent] = useState<ContentOptions | null>(null)
  const [contentValid, setContentValid] = useState(true)
  const [contentEvidence, setContentEvidence] = useState<PdfContentEvidence>()
  const [evidenceError, setEvidenceError] = useState<string>()
  const [evidenceRetry, setEvidenceRetry] = useState(0)
  const [ocrReviewed, setOcrReviewed] = useState(false)
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
  const isContent = operationMode === 'content'
  const validOptions = isContent ? contentValid : bookmarksValid && linksValid
  const hasChanges = isContent ? !!content && !!contentInspection?.capabilities.enabled && !!contentInspection.capabilities.engineBinding
    && contentInspection.blockReasons.length === 0 : pageMode !== null || bookmarks !== null || links.length > 0
  const canPreview = editable && validOptions && hasChanges
  const canExecute = !disabled && !busy && !job && (!!handle || (editable && !!plan && validOptions))
  const originalDraft = handle?.expectedRevision === workspaceRevision && handle?.sourceDocumentId === documentId
  const incompleteResult = job?.status === 2 && (!job.resultVersionId || !job.resultSha256)
  const requiresOcrReview = !!handle?.content?.parameters.ocr
  const canAdopt = !disabled && !busy && originalDraft && job?.status === 2 && !incompleteResult
    && !!job.resultVersionId && !!job.resultSha256 && !adopted && !job.adoption
    && (!handle?.content || (!!contentEvidence && (!requiresOcrReview || ocrReviewed)))
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

  const contentBinding = handle?.content?.engineBinding
  const contentSourceHash = handle?.content?.sourceSha256
  const confirmedContent = contentSignature(handle?.content?.parameters)
  useEffect(() => {
    setContentEvidence(undefined); setEvidenceError(undefined); setOcrReviewed(false)
    if (!contentBinding || !contentSourceHash || !confirmedContent || job?.status !== 2 || !job.resultSha256) return
    let cancelled = false
    void (async () => {
      try {
        const evidence: PdfContentEvidence = await apiFetch(`${base}/jobs/${encodeURIComponent(job.id)}/content-receipt`)
        if (cancelled || !active.current) return
        const requested = JSON.parse(confirmedContent) as ContentOptions
        if (evidence.engineBinding !== contentBinding || evidence.receipt.sourceSha256 !== contentSourceHash
          || evidence.receipt.outputSha256 !== job.resultSha256 || !/^[a-f0-9]{64}$/i.test(evidence.receiptSha256)
          || contentSignature(evidence.receipt.parameters) !== confirmedContent
          || (requested.ocr && (!evidence.receipt.ocr || evidence.receipt.ocr.language !== requested.ocr.language
            || evidence.receipt.ocr.dpi !== requested.ocr.dpi
            || JSON.stringify([...evidence.receipt.ocr.processedPages, ...evidence.receipt.ocr.alreadySearchablePages].sort((a, b) => a - b)) !== JSON.stringify(requested.ocr.pages)))
          || (requested.embedStandardFonts && !evidence.receipt.fonts)
          || (requested.targetVersion && evidence.receipt.outputVersion !== requested.targetVersion))
          throw new Error('处理证据与当前任务不一致，不能采用此结果。')
        setContentEvidence(evidence)
      } catch (caught) { if (!cancelled && active.current) setEvidenceError(message(caught)) }
    })()
    return () => { cancelled = true }
  }, [base, contentBinding, contentSourceHash, confirmedContent, evidenceRetry, job?.id, job?.status, job?.resultSha256])

  const invalidate = () => { inputGeneration.current++; setPlan(undefined); enqueueKey.current = undefined; setError(undefined) }
  const inspect = async () => {
    if (disabled || busy || running || workspaceRevision === undefined) return
    localStorage.setItem(handleKey, 'null'); setHandle(undefined)
    setBusy(true); invalidate(); setInspection(undefined); setJob(undefined); setAdopted(false)
    setPageMode(null); setBookmarks(null); setLinks([]); setContent(null); setContentInspection(undefined)
    setContentEvidence(undefined); setEvidenceError(undefined); setOcrReviewed(false)
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
  const inspectContent = async () => {
    if (!editable || !inspection) return
    setBusy(true); invalidate(); setContentInspection(undefined); setContent(null)
    const generation = inputGeneration.current
    try {
      const result: PdfContentInspection = await apiFetch(`${base}/placements/${encodeURIComponent(placementId)}/content-inspection`,
        buildJsonRequestInit('POST', { sourceVersionId: inspection.sourceVersionId, expectedRevision: workspaceRevision }))
      if (!active.current || generation !== inputGeneration.current) return
      if (result.sourceVersionId !== inspection.sourceVersionId || result.sha256 !== inspection.sha256
        || (result.capabilities.enabled && result.pageCount !== navigation?.pageCount)) throw new Error('内容预检与当前源版本不一致，请重新读取。')
      setContentInspection(result)
    } catch (caught) { if (active.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const preview = async () => {
    if (!canPreview || !inspection) return
    const generation = ++inputGeneration.current
    setBusy(true); setError(undefined); setPlan(undefined); enqueueKey.current = undefined
    const parameters: PdfProcessingParameters = isContent
      ? { pageMode: null, bookmarks: null, links: null, referenceEdits: null, content }
      : { pageMode, bookmarks: bookmarks === null ? null : [...bookmarks], links: [...links], referenceEdits: null, content: null }
    try {
      const result: PdfProcessingPlan = await apiFetch(`${base}/placements/${encodeURIComponent(placementId)}/plans`,
        buildJsonRequestInit('POST', { sourceVersionId: inspection.sourceVersionId, expectedRevision: workspaceRevision, parameters }))
      if (!active.current || generation !== inputGeneration.current) return
      if (result.applicationId !== appId || result.sequenceNumber !== seqNumber || result.placementId !== placementId
        || result.sourceVersionId !== inspection.sourceVersionId || result.sourceDocumentId !== documentId
        || result.expectedWorkspaceRevision !== workspaceRevision || result.sourceSha256 !== inspection.sha256
        || result.sourceMd5 !== inspection.md5
        || result.engineVersion !== (isContent ? contentInspection?.capabilities.engineBinding : navigation?.engineVersion) || !result.planDigest)
        throw new Error('处理预览与当前源版本不一致，请重新读取 PDF 导航。')
      if (isContent) {
        const reviewed = JSON.parse(result.parametersJson) as PdfProcessingParameters | null
        if (!reviewed || reviewed.pageMode != null || reviewed.bookmarks != null || reviewed.links != null || reviewed.referenceEdits != null
          || !contentSignature(reviewed.content) || contentSignature(reviewed.content) !== contentSignature(content))
          throw new Error('预览参数与已选择的内容操作不一致，请重新生成预览。')
      }
      setPlan(result); enqueueKey.current = crypto.randomUUID()
    } catch (caught) { if (active.current && generation === inputGeneration.current) setError(message(caught)) }
    finally { if (active.current) setBusy(false) }
  }
  const execute = async () => {
    if (!canExecute || (!handle && (!plan || !enqueueKey.current))) return
    setBusy(true); setError(undefined)
    const request = handle ?? { planId: plan!.id, planDigest: plan!.planDigest, idempotencyKey: enqueueKey.current!,
      expectedRevision: plan!.expectedWorkspaceRevision, sourceDocumentId: plan!.sourceDocumentId,
      ...(isContent ? { content: { engineBinding: plan!.engineVersion, sourceSha256: plan!.sourceSha256, parameters: content! } } : {}) }
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
  return <section aria-label="PDF 处理" className="rounded border bg-white p-4 space-y-3">
    <h3 className="font-medium">PDF 导航与内容处理</h3>
    <p>读取当前内容版本，选择导航或内容处理并预览。处理结果经过检查后，由你选择是否采用到草稿。</p>
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
      <div className="flex gap-3" aria-label="处理类型">
        <button type="button" aria-pressed={!isContent} disabled={!editable} onClick={() => { setOperationMode('navigation'); invalidate() }}>导航处理</button>
        <button type="button" aria-pressed={isContent} disabled={!editable} onClick={() => { setOperationMode('content'); invalidate() }}>内容处理</button>
      </div>
      {isContent ? <div className="space-y-3">
        <button type="button" disabled={!editable} onClick={() => { void inspectContent() }}>读取内容处理预检</button>
        {contentInspection && <>
          <p>PDF {contentInspection.sourceVersion} · 扫描页：{contentInspection.scannedPages.join(', ') || '无'} · 已有文本页：{contentInspection.searchablePages.join(', ') || '无'}</p>
          {contentInspection.missingFonts.length > 0 && <p>缺失字体：{contentInspection.missingFonts.map(font => `${font.name}（${font.qualified ? '支持嵌入' : '尚未验证'}）`).join('；')}</p>}
          {contentInspection.blockReasons.length > 0 && <p role="alert">{contentInspection.blockReasons.join('；')}</p>}
          <PdfContentEditor key={contentInspection.sourceVersionId} value={content} pageCount={contentInspection.pageCount}
            disabled={!editable || !contentInspection.capabilities.enabled || contentInspection.blockReasons.length > 0}
            availability={{ ocr: contentInspection.capabilities.ocrAvailable,
              fonts: contentInspection.capabilities.fontEmbeddingAvailable && contentInspection.missingFonts.every(font => font.qualified),
              version: contentInspection.capabilities.versionNormalizationAvailable,
              reason: contentInspection.capabilities.blockReasons.join('；') || null }}
            onChange={value => { setContent(value); invalidate() }} onValidityChange={setContentValid} />
          {contentInspection.capabilities.unverifiedProperties.length > 0 && <p>仍需验证：{contentInspection.capabilities.unverifiedProperties.join('；')}</p>}
        </>}
      </div> : <>
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
      </>}
      <div className="flex gap-3">
        <button type="button" disabled={!canPreview} onClick={() => { void preview() }}>生成处理预览</button>
        <button type="button" disabled={!canExecute} onClick={() => { void execute() }}>执行已预览处理</button>
      </div>
    </>}
    {plan && <div className="space-y-2">
      <p>计划摘要 {plan.planDigest.slice(0, 12)}… · 草稿修订 {plan.expectedWorkspaceRevision}</p>
      {isContent ? <p>内容处理：{content?.ocr ? `英语 OCR，第 ${content.ocr.pages.join(', ')} 页；` : ''}{content?.embedStandardFonts ? '嵌入支持的标准字体；' : ''}{content?.targetVersion ? `输出 PDF ${content.targetVersion}` : ''}</p>
        : <p>初始视图：{pageMode ?? '保留原值'}；书签：{bookmarks === null ? '保留原值' : `替换为 ${bookmarks.length} 个`}；修改链接：{links.length} 个。</p>}
    </div>}
    {job && <div className="space-y-2" aria-label="处理任务">
      <p role="status">{incompleteResult ? '处理完成但结果不完整' : adopted || job.adoption ? '处理结果已采用到草稿' : statusText[job.status]} · 尝试 {job.attemptCount}</p>
      {incompleteResult && <p role="alert">处理完成但结果缺少版本或 SHA-256 摘要，无法下载或采用。</p>}
      {job.failureMessage && <p role="alert">{job.failureMessage}</p>}
      {job.cancellationRequested && running && <p>已请求取消，正在等待任务结束。</p>}
      {handle?.content && job.status === 2 && <div className="space-y-2">
        {!contentEvidence && !evidenceError && <p role="status">正在读取内容处理证据…</p>}
        {evidenceError && <><p role="alert">{evidenceError}</p><button type="button" onClick={() => setEvidenceRetry(value => value + 1)}>重新读取处理证据</button></>}
        {contentEvidence && <>
          <p>结果 PDF {contentEvidence.receipt.outputVersion} · 用时 {(contentEvidence.resources.wallMilliseconds / 1000).toFixed(1)} 秒 · 峰值内存 {(contentEvidence.resources.peakMemoryBytes / 1048576).toFixed(1)} MiB</p>
          {contentEvidence.receipt.ocr && <p>OCR 完成页：{contentEvidence.receipt.ocr.processedPages.join(', ') || '无'}；已有文本而跳过：{contentEvidence.receipt.ocr.alreadySearchablePages.join(', ') || '无'}。</p>}
          {contentEvidence.receipt.fonts && <p>已核对嵌入字体：{contentEvidence.receipt.fonts.embedded.map(font => font.name).join(', ') || '原字体已嵌入'}。</p>}
          <p>区域合规与 PDF/A：尚未验证。</p>
          {requiresOcrReview && <label><input type="checkbox" aria-label="我已核对 OCR 文本和阅读顺序"
            disabled={disabled || busy || adopted || !!job.adoption} checked={ocrReviewed} onChange={event => setOcrReviewed(event.target.checked)} /> 我已核对 OCR 文本和阅读顺序</label>}
        </>}
      </div>}
      {job.resultVersionId && job.resultSha256 && <button type="button" disabled={downloading}
        onClick={() => { void download(job.resultVersionId!, job.resultSha256!, 'processed.pdf') }}>下载处理结果</button>}
      <div className="flex gap-3">
        <button type="button" disabled={disabled || busy || !running || job.cancellationRequested} onClick={() => { void cancel() }}>取消处理</button>
        <button type="button" disabled={!canAdopt} onClick={() => { void adopt() }}>采用处理结果</button>
      </div>
    </div>}
  </section>
}
