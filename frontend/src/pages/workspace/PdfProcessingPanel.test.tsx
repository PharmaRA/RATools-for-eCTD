import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { PdfProcessingPanel } from './PdfProcessingPanel'
import type { PdfProcessingPlan, ProcessingInspection, ProcessingJobView } from '../../api/generated'

const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.unstubAllGlobals(); localStorage.clear() })
const inspection: ProcessingInspection = {
  sourceVersionId: 'source-version', sourceDocumentId: 'document', workspaceRevision: 7,
  sha256: 'a'.repeat(64), md5: 'b'.repeat(32), href: 'm5/report.pdf',
  navigation: { engineVersion: 'qpdf-12.4.1', pageCount: 2, pageMode: 'UseNone', bookmarks: [], links: [],
    namedDestinations: [], isEncrypted: false, hasSignature: false, signatureValidityKnown: true,
    navigationComplete: true, destinationsComplete: true, incompleteReasons: [] },
}
const plan: PdfProcessingPlan = { id: 'plan', sourceVersionId: 'source-version', sourceDocumentId: 'document',
  applicationId: 'application', placementId: 'placement', sequenceNumber: '0001', expectedWorkspaceRevision: 7,
  sourceSha256: inspection.sha256, sourceMd5: inspection.md5, sourcePageCount: 2,
  parametersJson: '{"pageMode":"UseOutlines","bookmarks":null,"links":[]}', planDigest: 'c'.repeat(64),
  engineVersion: 'qpdf-12.4.1', createdUtc: '2026-11-05T00:00:00Z', contextJson: '{}' }
const completed: ProcessingJobView = { id: 'job', planId: 'plan', status: 2, stage: 'completed', attemptCount: 1,
  cancellationRequested: false, createdUtc: plan.createdUtc, completedUtc: plan.createdUtc, failureCode: null,
  failureMessage: null, resultVersionId: 'result-version', resultSha256: 'd'.repeat(64), resultMd5: 'e'.repeat(32), adoption: null! }
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } })
function render(onAdopted = vi.fn(), disabled = false) {
  const container = document.createElement('div'); document.body.appendChild(container)
  const root = createRoot(container)
  const update = (revision = 7, documentId = 'document') => act(() => root.render(<PdfProcessingPanel appId="application"
    seqNumber="0001" placementId="placement" documentId={documentId} workspaceRevision={revision}
    disabled={disabled} remoteTargets={[]} onAdopted={onAdopted} />))
  update(); disposers.push(() => { act(() => root.unmount()); container.remove() })
  return update
}
const button = (name: string) => {
  const found = Array.from(document.querySelectorAll('button')).find(item => item.textContent === name)
  if (!found) throw new Error(`Button ${name} not found`)
  return found
}
const click = async (name: string) => act(async () => { button(name).click(); await Promise.resolve() })
const setMode = (mode = 'UseOutlines') => act(() => {
  const input = document.querySelector<HTMLSelectElement>('select[aria-label="PDF 初始视图"]')!
  input.value = mode; input.dispatchEvent(new Event('change', { bubbles: true }))
})
async function prepare() { await click('读取 PDF 导航'); setMode(); await click('生成处理预览') }

it('uses the reviewed source and plan and waits for explicit adoption after completion', async () => {
  const adopted = vi.fn()
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json(completed)).mockResolvedValueOnce(json({ jobId: 'job', workspaceRevision: 8 }))
  vi.stubGlobal('fetch', fetch); render(adopted); await prepare(); await click('执行已预览处理')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(adopted).not.toHaveBeenCalled()
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toMatchObject({ sourceVersionId: 'source-version', expectedRevision: 7,
    parameters: { pageMode: 'UseOutlines', bookmarks: null, links: [] } })
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toMatchObject({ planId: 'plan', planDigest: plan.planDigest })
  await click('采用处理结果')
  expect(fetch.mock.calls[3][0]).toContain('/jobs/job/adopt')
  expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual({ expectedRevision: 7 })
  expect(adopted).toHaveBeenCalledOnce()
})

it.each([
  ['version', { resultVersionId: null, resultSha256: completed.resultSha256 }],
  ['sha256', { resultVersionId: completed.resultVersionId, resultSha256: null }],
])('blocks adoption and reports an incomplete completed result when %s is missing', async (_missing, result) => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json({ ...completed, ...result }))
  vi.stubGlobal('fetch', fetch); render(); await prepare(); await click('执行已预览处理')
  expect(button('采用处理结果').disabled).toBe(true)
  expect(document.body.textContent).toContain('结果缺少')
  expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('结果缺少')
})

it('withdraws the reviewed plan when parameters change', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan)))
  render(); await prepare(); expect(button('执行已预览处理').disabled).toBe(false)
  setMode('UseThumbs'); expect(button('执行已预览处理').disabled).toBe(true)
})

it('reuses the same idempotency key after an enqueue response is lost', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockRejectedValueOnce(new TypeError('connection interrupted')).mockResolvedValueOnce(json(completed))
  vi.stubGlobal('fetch', fetch); render(); await prepare(); await click('执行已预览处理'); await click('执行已预览处理')
  const first = JSON.parse(fetch.mock.calls[2][1].body)
  expect(first.idempotencyKey).toBeTruthy()
  expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual(first)
})

it('discards a pending preview when the workspace revision changes', async () => {
  let resolvePreview!: (value: Response) => void
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { resolvePreview = resolve })))
  const update = render(); await click('读取 PDF 导航'); setMode(); await click('生成处理预览')
  update(8); await act(async () => resolvePreview(json(plan)))
  expect(document.querySelector('select[aria-label="PDF 初始视图"]')).toBeNull()
  expect(document.body.textContent).not.toContain('计划摘要')
})

it('blocks editing signed or incompletely inspected files', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json({ ...inspection,
    navigation: { ...inspection.navigation, hasSignature: true, signatureValidityKnown: false } })))
  render(); await click('读取 PDF 导航')
  expect(document.body.textContent).toContain('签名')
  expect(button('生成处理预览').disabled).toBe(true)
})

it('cancels a queued job without adopting any version', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json({ ...completed, status: 0, resultVersionId: null, stage: 'queued' }))
    .mockResolvedValueOnce(json({ ...completed, status: 4, resultVersionId: null, stage: 'cancelled' }))
  vi.stubGlobal('fetch', fetch); render(); await prepare(); await click('执行已预览处理'); await click('取消处理')
  expect(fetch.mock.calls[3][0]).toContain('/jobs/job/cancel')
  expect(document.body.textContent).toContain('已取消')
  expect(button('采用处理结果').disabled).toBe(true)
})

it('retains a completed task across revision changes and keeps adoption disabled for the stale draft', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json(completed)).mockResolvedValueOnce(json(completed))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览处理')
  update(8); await click('刷新处理状态')
  expect(fetch.mock.calls[3][0]).toContain('/jobs/job')
  expect(button('下载处理结果').disabled).toBe(false)
  expect(button('采用处理结果').disabled).toBe(true)
  expect(document.body.textContent).toContain('草稿修订已变化')
})

it('retains the exact enqueue request across remount after a lost response', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockRejectedValueOnce(new TypeError('response lost')).mockResolvedValueOnce(json(completed))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览处理')
  const request = JSON.parse(fetch.mock.calls[2][1].body)
  update(7, 'temporarily-selected-document'); update()
  await click('恢复处理任务')
  expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual(request)
  expect(button('采用处理结果').disabled).toBe(false)
})

it('keeps a recovered handle after a definitive enqueue rejection and offers safe abandonment', async () => {
  localStorage.setItem('ratools.pdf-processing:["application","0001","placement","document"]', JSON.stringify({
    planId: plan.id, planDigest: plan.planDigest, idempotencyKey: 'stable-key', expectedRevision: 7,
    sourceDocumentId: 'document',
  }))
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ message: 'request conflict' }), { status: 409 })))
  render(); await click('恢复处理任务')
  expect(document.body.textContent).toContain('request conflict')
  expect(button('放弃本地处理请求').disabled).toBe(false)
  expect(button('重新检查 PDF 导航').disabled).toBe(false)
  expect(localStorage.getItem('ratools.pdf-processing:["application","0001","placement","document"]')).not.toBeNull()
  await click('放弃本地处理请求')
  expect(localStorage.getItem('ratools.pdf-processing:["application","0001","placement","document"]')).toBe('null')
})

it('keeps cancellation available while a source download is pending', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json({ ...completed, status: 1, resultVersionId: null }))
    .mockImplementationOnce(() => new Promise<Response>(() => {}))
    .mockResolvedValueOnce(json({ ...completed, status: 4, resultVersionId: null }))
  vi.stubGlobal('fetch', fetch); render(); await prepare(); await click('执行已预览处理'); await click('下载源版本')
  expect(button('取消处理').disabled).toBe(false)
  await click('取消处理')
  expect(fetch.mock.calls[4][0]).toContain('/jobs/job/cancel')
  expect(document.body.textContent).toContain('已取消')
})

it('allows a fresh inspection after the server definitively rejects the first enqueue', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'preview expired' }), { status: 409 })))
  render(); await prepare(); await click('执行已预览处理')
  expect(button('读取 PDF 导航').disabled).toBe(false)
  expect(document.body.textContent).toContain('preview expired')
})

it('notifies the workspace of adoption even after the selected document changes', async () => {
  let resolveAdoption!: (value: Response) => void
  const adopted = vi.fn()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockResolvedValueOnce(json(completed))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveAdoption = resolve })))
  const update = render(adopted); await prepare(); await click('执行已预览处理'); await click('采用处理结果')
  update(7, 'other-document')
  await act(async () => resolveAdoption(json({ jobId: 'job', workspaceRevision: 8 })))
  expect(adopted).toHaveBeenCalledOnce()
})

it('does not overwrite a newer persisted request with an old delayed enqueue response', async () => {
  let resolveOld!: (value: Response) => void
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(plan))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveOld = resolve }))
    .mockResolvedValueOnce(json(completed)).mockResolvedValueOnce(json(inspection))
    .mockResolvedValueOnce(json({ ...plan, id: 'new-plan', planDigest: 'f'.repeat(64) }))
    .mockResolvedValueOnce(json({ ...completed, id: 'new-job', planId: 'new-plan' }))
    .mockResolvedValueOnce(json({ ...completed, id: 'new-job', planId: 'new-plan' }))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepare(); await click('执行已预览处理')
  update(7, 'other-document'); update(); await click('恢复处理任务')
  await prepare(); await click('执行已预览处理')
  await act(async () => resolveOld(json(completed)))
  update(8); await click('刷新处理状态')
  expect(fetch.mock.calls[7][0]).toContain('/jobs/new-job')
})

it('disables inspection and mutation controls for a finalized read-only workspace', async () => {
  const fetch = vi.fn()
  vi.stubGlobal('fetch', fetch)
  render(vi.fn(), true)

  expect(button('读取 PDF 导航').disabled).toBe(true)
  expect(fetch).not.toHaveBeenCalled()
})

const contentFacts = { sourceVersionId: inspection.sourceVersionId, sha256: inspection.sha256, pageCount: 2,
  sourceVersion: '1.7', searchablePages: [], scannedPages: [1, 2], missingFonts: [], blockReasons: [],
  capabilities: { enabled: true, engineBinding: 'content-binding', ocrAvailable: true, fontEmbeddingAvailable: true,
    versionNormalizationAvailable: true, language: 'eng', dpi: 150, maxPages: 500, maxPixels: 24000000,
    blockReasons: [], unverifiedProperties: ['PDF/A', '区域合规'] } }
const contentParameters = { ocr: { language: 'eng', pages: [1, 2], dpi: 150 }, embedStandardFonts: false, targetVersion: null }
const contentPlan = { ...plan, engineVersion: 'content-binding', parametersJson: JSON.stringify({
  pageMode: null, bookmarks: null, links: null, referenceEdits: null, content: contentParameters }) }
const contentEvidence = { engineBinding: 'content-binding', receiptSha256: 'f'.repeat(64),
  receipt: { sourceSha256: inspection.sha256, outputSha256: completed.resultSha256, parameters: contentParameters,
    ocr: { processedPages: [1, 2], alreadySearchablePages: [], requiresTextReview: true, language: 'eng', dpi: 150 },
    fonts: null, outputVersion: '1.7', regulatoryConformance: 'NotEvaluated' },
  resources: { wallMilliseconds: 2000, peakMemoryBytes: 104857600 } }
const toggle = (label: string) => act(() => document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!.click())
async function prepareContent() {
  await click('读取 PDF 导航'); await click('内容处理'); await click('读取内容处理预检')
  toggle('对扫描页进行 OCR'); await click('生成处理预览')
}

it('binds a content-only preview to inspected capabilities and excludes navigation mutations', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts)).mockResolvedValueOnce(json(contentPlan))
  vi.stubGlobal('fetch', fetch); render(); await prepareContent()
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ sourceVersionId: inspection.sourceVersionId, expectedRevision: 7,
    parameters: { pageMode: null, bookmarks: null, links: null, referenceEdits: null,
      content: { ocr: { language: 'eng', pages: [1, 2], dpi: 150 }, embedStandardFonts: false, targetVersion: null } } })
  expect(button('执行已预览处理').disabled).toBe(false)
  expect(document.body.textContent).toContain('扫描页：1, 2')
})

it('requires verified content evidence and explicit OCR review before adoption', async () => {
  const adopted = vi.fn()
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json(contentPlan)).mockResolvedValueOnce(json(completed)).mockResolvedValueOnce(json(contentEvidence))
    .mockResolvedValueOnce(json({ jobId: 'job', workspaceRevision: 8 }))
  vi.stubGlobal('fetch', fetch); render(adopted); await prepareContent(); await click('执行已预览处理')
  expect(fetch.mock.calls[4][0]).toContain('/jobs/job/content-receipt')
  expect(button('采用处理结果').disabled).toBe(true)
  toggle('我已核对 OCR 文本和阅读顺序')
  expect(button('采用处理结果').disabled).toBe(false)
  await click('采用处理结果')
  expect(adopted).toHaveBeenCalledOnce()
})

it('rejects a mismatched content receipt and keeps adoption blocked', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json(contentPlan)).mockResolvedValueOnce(json(completed))
    .mockResolvedValueOnce(json({ ...contentEvidence, receipt: { ...contentEvidence.receipt, outputSha256: 'bad-hash' } }))
  vi.stubGlobal('fetch', fetch); render(); await prepareContent(); await click('执行已预览处理')
  expect(button('采用处理结果').disabled).toBe(true)
  expect(document.body.textContent).toContain('处理证据与当前任务不一致')
})

it('rejects a content plan returned for a different engine binding', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json({ ...contentPlan, engineVersion: 'unexpected-engine' }))
  vi.stubGlobal('fetch', fetch); render(); await prepareContent()
  expect(button('执行已预览处理').disabled).toBe(true)
  expect(document.body.textContent).toContain('处理预览与当前源版本不一致')
})

it('shows blocked capabilities and does not permit a content preview', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json({ ...contentFacts, pageCount: 0, sourceVersion: 'Unknown',
    capabilities: { ...contentFacts.capabilities, enabled: false, ocrAvailable: false, fontEmbeddingAvailable: false,
      versionNormalizationAvailable: false, engineBinding: null, blockReasons: ['内容引擎尚未配置'] } }))
  vi.stubGlobal('fetch', fetch); render(); await click('读取 PDF 导航'); await click('内容处理'); await click('读取内容处理预检')
  expect(document.body.textContent).toContain('内容引擎尚未配置')
  expect(button('生成处理预览').disabled).toBe(true)
})

it.each([
  ['different pages', { pageMode: null, content: { ...contentParameters, ocr: { ...contentParameters.ocr, pages: [2] } } }],
  ['mixed navigation', { pageMode: 'FullScreen', content: contentParameters }],
  ['missing content', { pageMode: null }],
])('rejects returned content plans with %s instead of executing different operations', async (_reason, parameters) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json({ ...contentPlan, parametersJson: JSON.stringify(parameters) })))
  render(); await prepareContent()
  expect(button('执行已预览处理').disabled).toBe(true)
  expect(document.body.textContent).toContain('预览参数与已选择的内容操作不一致')
})

it('rejects a receipt that substitutes another operation for the requested OCR', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json(contentPlan)).mockResolvedValueOnce(json(completed))
    .mockResolvedValueOnce(json({ ...contentEvidence, receipt: { ...contentEvidence.receipt, ocr: null,
      parameters: { ocr: null, embedStandardFonts: true, targetVersion: null } } })))
  render(); await prepareContent(); await click('执行已预览处理')
  expect(button('采用处理结果').disabled).toBe(true)
  expect(document.body.textContent).toContain('处理证据与当前任务不一致')
})

it('keeps the confirmed OCR review gate after response loss and remount even when receipt flags say otherwise', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(inspection)).mockResolvedValueOnce(json(contentFacts))
    .mockResolvedValueOnce(json(contentPlan)).mockRejectedValueOnce(new TypeError('response lost'))
    .mockResolvedValueOnce(json(completed)).mockResolvedValueOnce(json({ ...contentEvidence,
      receipt: { ...contentEvidence.receipt, ocr: { ...contentEvidence.receipt.ocr, requiresTextReview: false } } }))
  vi.stubGlobal('fetch', fetch); const update = render(); await prepareContent(); await click('执行已预览处理')
  update(7, 'other-document'); update(); await click('恢复处理任务')
  expect(JSON.parse(fetch.mock.calls[4][1].body)).toEqual(JSON.parse(fetch.mock.calls[3][1].body))
  expect(button('采用处理结果').disabled).toBe(true)
  toggle('我已核对 OCR 文本和阅读顺序')
  expect(button('采用处理结果').disabled).toBe(false)
})
