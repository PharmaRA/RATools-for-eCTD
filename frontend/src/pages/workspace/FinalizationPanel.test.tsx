import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FinalizedSequence, PackageRunSummary, ReleaseManifest, StandardsSnapshotView, ValidationReport } from '../../api/generated'
import * as actions from '../../finalizationActions'
import { FinalizationPanel } from './FinalizationPanel'
import { loadValidationRuns } from '../../packageValidationActions'

vi.mock('../../packageValidationActions', async importOriginal => ({
  ...await importOriginal<typeof import('../../packageValidationActions')>(), loadValidationRuns: vi.fn(),
}))

vi.mock('../../finalizationActions', async importOriginal => ({
  ...await importOriginal<typeof import('../../finalizationActions')>(),
  loadFinalizations: vi.fn(), loadFinalizationEvidence: vi.fn(), loadReleaseManifest: vi.fn(),
  loadDeliveries: vi.fn(), loadDeliveryReceipts: vi.fn(), finalizeSequence: vi.fn(),
  registerDelivery: vi.fn(), registerDeliveryReceipt: vi.fn(), openFinalizedDraft: vi.fn(), downloadRelease: vi.fn(),
}))

const binding = { inputManifestDigest: 'a'.repeat(64), historyBaselineDigest: 'b'.repeat(64), profileSnapshotId: 'profile',
  rulesDigest: 'c'.repeat(64), resourceLimitsDigest: 'd'.repeat(64), engineVersion: 'test',
  validationMode: 'Development' as const, standardsSnapshotId: 'standard' }
const evidence = {
  run: { id: 'run-1', applicationId: 'app', sequenceNumber: '0000', status: 'Completed', reportAvailable: true, binding } as PackageRunSummary,
  report: { binding, runStatus: 'Completed', executionCompleted: true, isReadyForFinalization: false,
    blockingReasons: ['ProfileNotQualified'] } as ValidationReport,
  standards: { id: 'standard', ruleProfileAlias: 'profile', ichEctdVersion: '3.2.2', regionalModule1Version: '3.1.1' } as StandardsSnapshotView,
}
const snapshot = { id: 'snapshot', applicationId: 'app', sequenceNumber: '0000', sourceWorkspaceRevision: 7,
  manifestDigest: 'e'.repeat(64), validationMode: 'Development', isReadyForFinalization: false,
  standardsProfileSnapshotId: 'profile', ichEctdVersion: '3.2.2', regionalModule1Version: '3.1.1',
  historyBaselineDigest: binding.historyBaselineDigest, validationRunId: 'run-1', blockingReasons: ['ProfileNotQualified'], createdUtc: '2026-11-07T00:00:00Z' } as FinalizedSequence
const manifest = { digest: snapshot.manifestDigest, applicationId: 'app', sequenceNumber: '0000', sourceWorkspaceRevision: 7,
  files: [{ relativePath: 'delivery-package.zip', kind: 3, sha256: 'f'.repeat(64), length: 17, md5: '0'.repeat(32) }],
  nodes: [], leaves: [], lifecycle: [] } as unknown as ReleaseManifest

let container: HTMLDivElement
let root: Root
let client: QueryClient
const changed = vi.fn(async () => {})
const render = async (revision = 7) => act(async () => root.render(<QueryClientProvider client={client}>
  <FinalizationPanel appId="app" seqNumber="0000" workspaceRevision={revision} onChanged={changed} />
</QueryClientProvider>))
const wait = async (condition: () => boolean) => {
  for (let index = 0; index < 100; index++) {
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
    if (condition()) return
  }
  throw new Error(`UI condition not observed: ${container.textContent}`)
}
const click = async (label: string) => {
  const button = [...container.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === label)
  if (!button) throw new Error(`Button absent: ${label}`)
  await act(async () => button.click())
}
const fill = async (name: string, value: string) => {
  const element = container.querySelector<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(`[name="${name}"]`)!
  if (!element) throw new Error(`Input absent: ${name}`)
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype
    : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  await act(async () => {
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value)
    element.dispatchEvent(new Event('input', { bubbles: true }))
    element.dispatchEvent(new Event('change', { bubbles: true }))
  })
}
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
  changed.mockResolvedValue(undefined)
  vi.mocked(actions.loadFinalizations).mockResolvedValue([])
  vi.mocked(actions.loadFinalizationEvidence).mockResolvedValue(evidence)
  vi.mocked(actions.loadReleaseManifest).mockResolvedValue(manifest)
  vi.mocked(actions.loadDeliveries).mockResolvedValue([])
  vi.mocked(actions.loadDeliveryReceipts).mockResolvedValue([])
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); client.clear() })

describe('FinalizationPanel', () => {
  it('selects a completed run from this sequence and keeps history pagination available', async () => {
    vi.mocked(loadValidationRuns).mockResolvedValue({ items: [evidence.run, { ...evidence.run, id: 'other', sequenceNumber: '0001' }], totalCount: 21, offset: 0, limit: 20 })
    await render(); await click('选择验包历史')
    await wait(() => container.textContent!.includes('使用此运行'))
    expect([...container.querySelectorAll('button')].filter(button => button.textContent === '使用此运行')).toHaveLength(1)
    await click('使用此运行')
    await wait(() => !!container.querySelector('[data-testid="finalization-evidence"]'))
    expect(actions.loadFinalizationEvidence).toHaveBeenCalledWith('run-1', expect.any(AbortSignal))
    await click('下一页运行')
    await wait(() => vi.mocked(loadValidationRuns).mock.calls.some(call => call[0] === 20 && call[1] === 'app'))
  })
  it('reuses the request identity after a lost response, reselection and panel remount', async () => {
    vi.mocked(actions.finalizeSequence).mockRejectedValue(new Error('响应丢失'))
    await render(); await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => !!container.querySelector('[name="confirmFinalization"]'))
    await act(async () => container.querySelector<HTMLInputElement>('[name="confirmFinalization"]')!.click())
    await click('保存开发快照'); await wait(() => container.textContent!.includes('响应丢失'))
    await fill('validationRunId', 'run-2'); await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => !!container.querySelector('[name="confirmFinalization"]'))
    await act(async () => container.querySelector<HTMLInputElement>('[name="confirmFinalization"]')!.click())
    await click('保存开发快照'); await wait(() => vi.mocked(actions.finalizeSequence).mock.calls.length === 2)
    await act(async () => root.render(<div />))
    await render(); await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => !!container.querySelector('[name="confirmFinalization"]'))
    await act(async () => container.querySelector<HTMLInputElement>('[name="confirmFinalization"]')!.click())
    await click('保存开发快照'); await wait(() => vi.mocked(actions.finalizeSequence).mock.calls.length === 3)
    const calls = vi.mocked(actions.finalizeSequence).mock.calls
    expect(calls[0]).toEqual(calls[1]); expect(calls[0]).toEqual(calls[2])
  })

  it('lets the operator retry workspace refresh after a successful draft creation', async () => {
    vi.mocked(actions.loadFinalizations).mockResolvedValue([snapshot])
    vi.mocked(actions.openFinalizedDraft).mockResolvedValue({ created: true } as Awaited<ReturnType<typeof actions.openFinalizedDraft>>)
    changed.mockRejectedValueOnce(new Error('工作区刷新失败')).mockResolvedValue(undefined)
    await act(async () => root.render(<QueryClientProvider client={client}><FinalizationPanel appId="app" seqNumber="0000"
      workspaceRevision={7} finalization={{ finalizedSequenceId: snapshot.id, sourceWorkspaceRevision: 7, isEditable: false, draftWorkspaceRevision: null }}
      onChanged={changed} /></QueryClientProvider>))
    await wait(() => container.textContent!.includes('查看快照')); await click('查看快照'); await click('创建新草稿修订')
    await wait(() => container.textContent!.includes('工作区刷新失败'))
    await click('重试刷新工作区'); await wait(() => changed.mock.calls.length === 2)
    expect(actions.openFinalizedDraft).toHaveBeenCalledTimes(1)
    expect(container.textContent).not.toContain('工作区刷新失败')
  })
  it('requires explicit confirmation and retries the same development request after a lost response', async () => {
    vi.mocked(actions.finalizeSequence).mockRejectedValueOnce(new Error('响应丢失')).mockResolvedValue({ sequence: snapshot, created: false })
    await render()
    await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => container.textContent!.includes('ProfileNotQualified'))
    expect(actions.finalizeSequence).not.toHaveBeenCalled()
    const save = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('保存开发快照'))!
    expect(save.disabled).toBe(true)
    await act(async () => container.querySelector<HTMLInputElement>('[name="confirmFinalization"]')!.click())
    await click('保存开发快照'); await wait(() => container.textContent!.includes('响应丢失'))
    await click('保存开发快照'); await wait(() => vi.mocked(actions.finalizeSequence).mock.calls.length === 2)
    const calls = vi.mocked(actions.finalizeSequence).mock.calls
    expect(calls[0]).toEqual(calls[1])
    expect(calls[0][2]).toMatchObject({ expectedRevision: 7, packageValidationRunId: 'run-1', historyBaselineDigest: binding.historyBaselineDigest })
    await wait(() => container.textContent!.includes('快照已保存'))
  })
  it('keeps non-ready Formal reports blocked with their reasons', async () => {
    const formal = { ...binding, validationMode: 'Formal' as const }
    vi.mocked(actions.loadFinalizationEvidence).mockResolvedValue({ ...evidence,
      run: { ...evidence.run, binding: formal }, report: { ...evidence.report, binding: formal } })
    await render(); await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => container.textContent!.includes('ProfileNotQualified'))
    expect(actions.finalizeSequence).not.toHaveBeenCalled()
    const save = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('正式定稿'))
    expect(!save || save.disabled).toBe(true)
  })
  it('invalidates selected evidence when the workspace revision changes', async () => {
    await render(); await fill('validationRunId', 'run-1'); await click('读取验证证据')
    await wait(() => !!container.querySelector('[data-testid="finalization-evidence"]'))
    await render(8)
    expect(container.querySelector('[data-testid="finalization-evidence"]')).toBeNull()
    expect(actions.finalizeSequence).not.toHaveBeenCalled()
  })
  it('shows development snapshot downloads but refuses delivery registration', async () => {
    vi.mocked(actions.loadFinalizations).mockResolvedValue([snapshot])
    await render(); await wait(() => container.textContent!.includes('查看快照')); await click('查看快照')
    await wait(() => container.textContent!.includes('下载交付包'))
    expect(container.querySelector('[data-testid="delivery-form"]')).toBeNull()
    expect(container.textContent).toContain('开发快照不能登记交付')
    await click('下载交付包')
    expect(actions.downloadRelease).toHaveBeenCalledWith('snapshot', 'delivery-package.zip')
    expect(actions.registerDelivery).not.toHaveBeenCalled()
  })
  it('records a formal delivery with the ZIP hash and preserves the selected receipt category', async () => {
    vi.mocked(actions.loadFinalizations).mockResolvedValue([{ ...snapshot, validationMode: 'Formal', isReadyForFinalization: true }])
    const delivery = { id: 'delivery', channel: 'portal', recordedBy: 'operator', packageSha256: 'f'.repeat(64) } as Awaited<ReturnType<typeof actions.registerDelivery>>
    vi.mocked(actions.registerDelivery).mockImplementation(async () => {
      vi.mocked(actions.loadDeliveries).mockResolvedValue([delivery]); return delivery
    })
    vi.mocked(actions.registerDeliveryReceipt).mockResolvedValue({ id: 'receipt', kind: 0, outcome: '传输完成', rawText: '原始回执', rawTextSha256: 'a'.repeat(64) } as Awaited<ReturnType<typeof actions.registerDeliveryReceipt>>)
    await render(); await wait(() => container.textContent!.includes('查看快照')); await click('查看快照')
    await wait(() => !!container.querySelector('[data-testid="delivery-form"]'))
    await fill('channel', 'portal'); await fill('recordedBy', 'operator')
    await act(async () => { container.querySelector('[data-testid="delivery-form"]')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })) })
    await wait(() => vi.mocked(actions.registerDelivery).mock.calls.length === 1)
    expect(actions.registerDelivery).toHaveBeenCalledWith('snapshot', expect.objectContaining({ packageSha256: 'f'.repeat(64), channel: 'portal' }))
    await wait(() => container.textContent!.includes('查看回执')); await click('查看回执')
    await wait(() => !!container.querySelector('[data-testid="receipt-form"]'))
    await fill('kind', '0'); await fill('outcome', '传输完成'); await fill('rawText', '原始回执'); await fill('receiptRecordedBy', 'operator')
    await act(async () => { container.querySelector('[data-testid="receipt-form"]')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })) })
    await wait(() => vi.mocked(actions.registerDeliveryReceipt).mock.calls.length === 1)
    expect(actions.registerDeliveryReceipt).toHaveBeenCalledWith('delivery', expect.objectContaining({ kind: 0, outcome: '传输完成', rawText: '原始回执' }))
    expect(container.textContent).not.toContain('已获监管接受')
  })
})
