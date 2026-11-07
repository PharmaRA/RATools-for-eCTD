import { useState, type FormEvent } from 'react'
import { Alert, Button, Card, Space } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { FinalizedSequence, RegisterReceiptBody, ReleaseManifest, WorkspaceFinalizationState } from '../../api/generated'
import {
  buildDeliveryBody, buildFinalizationBody, downloadRelease, finalizeSequence, loadDeliveries,
  loadDeliveryReceipts, loadFinalizationEvidence, loadFinalizations, loadReleaseManifest,
  openFinalizedDraft, registerDelivery, registerDeliveryReceipt,
} from '../../finalizationActions'
import { loadValidationRuns } from '../../packageValidationActions'

export type FinalizationPanelProps = {
  appId: string; seqNumber: string; workspaceRevision: number | undefined
  finalization?: WorkspaceFinalizationState | null; onChanged: () => Promise<void>
}
const inputClass = 'border rounded px-2 py-1 w-full'
const text = (data: FormData, name: string) => String(data.get(name) ?? '').trim()
const ErrorNotice = ({ error }: { error: unknown }) => error
  ? <Alert type="error" title="操作未完成" description={error instanceof Error ? error.message : '请刷新记录并核对状态。'} /> : null
const receiptLabels = ['传输回执', '技术校验回执', '业务结果']
// Keep the mutation identity across panel remounts and response-loss retries.
// Only request IDs are stored here; no validation content or credentials.
const attemptKeys = new Map<string, string>()
const attemptIdentity = (scope: string) => {
  const storageKey = `ratools-finalization-attempt:${scope}`
  let key = attemptKeys.get(storageKey)
  try { key ??= sessionStorage.getItem(storageKey) ?? undefined } catch { /* Memory still survives panel remounts. */ }
  key ??= crypto.randomUUID()
  attemptKeys.set(storageKey, key)
  try { sessionStorage.setItem(storageKey, key) } catch { /* Storage may be unavailable. */ }
  return key
}

export const FinalizationPanel = (props: FinalizationPanelProps) => {
  const { appId, seqNumber, workspaceRevision, finalization, onChanged } = props
  const client = useQueryClient()
  const key = ['finalized-versions', appId, seqNumber]
  const versions = useQuery({ queryKey: key, queryFn: ({ signal }) => loadFinalizations(appId, seqNumber, signal) })
  const [selected, setSelected] = useState<FinalizedSequence>()
  const [notice, setNotice] = useState('')
  const [refreshError, setRefreshError] = useState<unknown>()
  const refreshWorkspace = async () => {
    try { await onChanged(); setRefreshError(undefined) } catch (error) { setRefreshError(error) }
  }
  const ordered = [...(versions.data ?? [])].sort((a, b) => b.sourceWorkspaceRevision - a.sourceWorkspaceRevision || b.createdUtc.localeCompare(a.createdUtc))
  const previous = selected ? ordered[ordered.findIndex(item => item.id === selected.id) + 1] : undefined
  return <Card title="定稿与交付" size="small" extra={<Button onClick={() => void versions.refetch()}>刷新快照</Button>}>
    <div className="space-y-3">
      <p>快照固定已验证的内容。导出、人工登记交付、传输回执、技术结果与业务结果分别保留。</p>
      <ErrorNotice error={versions.error || refreshError} />
      {!!refreshError && <Button onClick={() => void refreshWorkspace()}>重试刷新工作区</Button>}
      {notice && <Alert type="success" title={notice} />}
      {finalization?.isEditable === false
        ? <Alert type="info" title="当前工作区已定稿，修改前须从最新快照创建新草稿修订。" />
        : <Candidate key={`${appId}:${seqNumber}:${workspaceRevision}`} {...props} onCreated={snapshot => {
          setSelected(snapshot); setNotice(`快照已保存：${snapshot.id}`)
          client.setQueryData<FinalizedSequence[]>(key, current => [snapshot, ...(current ?? []).filter(item => item.id !== snapshot.id)])
          void refreshWorkspace()
        }} />}
      <ul aria-label="定稿版本" className="space-y-2">
        {ordered.map(snapshot => <li key={snapshot.id} className="border rounded p-2 break-all">
          修订 {snapshot.sourceWorkspaceRevision} · {snapshot.validationMode === 'Formal' ? '正式' : '开发'} · {snapshot.createdUtc} · {snapshot.id}{' '}
          <Button size="small" onClick={() => setSelected(snapshot)}>查看快照</Button>
        </li>)}
      </ul>
      {!versions.isFetching && !versions.error && !ordered.length && <p>尚无定稿快照。</p>}
      {selected && <SnapshotDetail key={selected.id} snapshot={selected} previous={previous?.id === selected.id ? undefined : previous}
        revision={workspaceRevision} canOpenDraft={finalization?.isEditable === false && finalization.finalizedSequenceId === selected.id}
        onDraft={() => { setNotice('新草稿修订已创建；原快照保持不变。'); void refreshWorkspace() }} />}
    </div>
  </Card>
}

const Candidate = (props: FinalizationPanelProps & { onCreated: (snapshot: FinalizedSequence) => void }) => {
  const [runId, setRunId] = useState('')
  const [selectedRun, setSelectedRun] = useState<string>()
  const [offset, setOffset] = useState<number>()
  const runs = useQuery({ queryKey: ['finalization-candidates', props.appId, offset], enabled: offset !== undefined,
    queryFn: ({ signal }) => loadValidationRuns(offset ?? 0, props.appId, signal) })
  return <section className="border rounded p-3 space-y-2" aria-label="定稿候选">
    <p>先在<a href="/package-validations" target="_blank" rel="noreferrer">独立验包</a>中完成当前交付物验证，再选择其运行 ID。标准版本和摘要将从归档证据读取。</p>
    <label>验证运行 ID<input name="validationRunId" className={inputClass} value={runId}
      onChange={event => { setRunId(event.target.value); setSelectedRun(undefined) }} /></label>
    <Button disabled={!runId.trim() || props.workspaceRevision === undefined} onClick={() => setSelectedRun(runId.trim())}>读取验证证据</Button>
    <Button onClick={() => offset === undefined ? setOffset(0) : void runs.refetch()}>选择验包历史</Button>
    <ErrorNotice error={runs.error} />
    {offset !== undefined && <div>
      <ul>{runs.data?.items.filter(run => run.sequenceNumber === props.seqNumber).map(run => <li key={run.id}>
        {run.createdUtc} · {run.binding.validationMode} · {run.status}{' '}
        <Button size="small" disabled={run.status !== 'Completed' || !run.reportAvailable}
          onClick={() => { setRunId(run.id); setSelectedRun(run.id) }}>使用此运行</Button>
      </li>)}</ul>
      <p>本页仅显示当前序列；申请内其他序列的运行占用历史分页位置。</p>
      <Button disabled={offset === 0 || runs.isFetching} onClick={() => setOffset(offset - 20)}>上一页运行</Button>
      <Button disabled={runs.isFetching || !runs.data || offset + 20 >= runs.data.totalCount}
        onClick={() => setOffset(offset + 20)}>下一页运行</Button>
    </div>}
    {selectedRun && <CandidateReview key={selectedRun} {...props} runId={selectedRun} />}
  </section>
}

const CandidateReview = ({ appId, seqNumber, workspaceRevision, runId, onCreated }:
  FinalizationPanelProps & { runId: string; onCreated: (snapshot: FinalizedSequence) => void }) => {
  const evidence = useQuery({ queryKey: ['finalization-evidence', appId, seqNumber, runId],
    queryFn: ({ signal }) => loadFinalizationEvidence(runId, signal), retry: false })
  const [confirmed, setConfirmed] = useState(false)
  const [attemptKey] = useState(() => attemptIdentity(JSON.stringify(['finalize', appId, seqNumber, workspaceRevision, runId])))
  let body: ReturnType<typeof buildFinalizationBody> | undefined
  let blocked: unknown
  if (evidence.data && !evidence.isError) {
    try { body = buildFinalizationBody(appId, seqNumber, workspaceRevision, evidence.data.run, evidence.data.report, evidence.data.standards, attemptKey) }
    catch (error) { blocked = error }
  }
  const save = useMutation({ mutationFn: () => {
    if (!confirmed || !body) throw new Error('请核对证据并明确确认。')
    return finalizeSequence(appId, seqNumber, body)
  }, onSuccess: result => onCreated(result.sequence) })
  return <div className="space-y-2">
    {evidence.isFetching && <p role="status">正在读取归档验证证据…</p>}
    <ErrorNotice error={evidence.error || blocked || save.error} />
    {evidence.error && <Button onClick={() => void evidence.refetch()}>重读验证证据</Button>}
    {evidence.data && <div data-testid="finalization-evidence" className="break-all">
      <p>修订 {workspaceRevision} · {evidence.data.run.binding.validationMode} · ICH {evidence.data.standards.ichEctdVersion} / M1 {evidence.data.standards.regionalModule1Version}</p>
      <p>标准快照：{evidence.data.standards.id}<br />历史摘要：{evidence.data.run.binding.historyBaselineDigest}</p>
      {evidence.data.report.blockingReasons.length > 0 && <Alert type="warning" title="未完成检查" description={evidence.data.report.blockingReasons.join('；')} />}
      {evidence.data.run.binding.validationMode === 'Development' && <p>开发快照用于复核，不能登记正式交付。</p>}
      <label><input name="confirmFinalization" type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> 我已核对当前修订及验证证据，确认保存此快照。</label>
      <div><Button loading={save.isPending} disabled={!confirmed || !body || evidence.isFetching || save.isSuccess} onClick={() => save.mutate()}>
        {evidence.data.run.binding.validationMode === 'Formal' ? '正式定稿' : '保存开发快照'}
      </Button></div>
    </div>}
  </div>
}

const SnapshotDetail = ({ snapshot, previous, revision, canOpenDraft, onDraft }: {
  snapshot: FinalizedSequence; previous?: FinalizedSequence; revision?: number; canOpenDraft: boolean; onDraft: () => void
}) => {
  const manifest = useQuery({ queryKey: ['release-manifest', snapshot.id], queryFn: ({ signal }) => loadReleaseManifest(snapshot.id, signal) })
  const prior = useQuery({ queryKey: ['release-manifest', previous?.id], enabled: !!previous,
    queryFn: ({ signal }) => loadReleaseManifest(previous!.id, signal) })
  const download = useMutation({ mutationFn: (kind: 'manifest' | 'validation-report.json' | 'delivery-package.zip') => downloadRelease(snapshot.id, kind) })
  return <section className="border rounded p-3 space-y-3" aria-label="快照详情">
    <h3 className="font-semibold">快照详情</h3>
    <p className="break-all">{snapshot.id} · 来源修订 {snapshot.sourceWorkspaceRevision}<br />验证运行：{snapshot.validationRunId}<br />
      ICH {snapshot.ichEctdVersion} / M1 {snapshot.regionalModule1Version} · {snapshot.standardsProfileSnapshotId}<br />
      历史基线：{snapshot.historyBaselineDigest}<br />清单摘要：{snapshot.manifestDigest}</p>
    <ErrorNotice error={manifest.error || download.error || prior.error} />
    <Space wrap>{(['manifest', 'validation-report.json', 'delivery-package.zip'] as const).map((kind, index) =>
      <Button key={kind} loading={download.isPending} disabled={!manifest.data || manifest.isError || manifest.isFetching}
        onClick={() => download.mutate(kind)}>{['下载清单', '下载验证报告', '下载交付包'][index]}</Button>)}</Space>
    {manifest.data && !manifest.isError && <>
      <details><summary>固定文件与摘要（{manifest.data.files.length}）</summary><ul>{manifest.data.files.map(file =>
        <li key={file.relativePath} className="break-all">{file.relativePath} · {file.length} B · {file.sha256}</li>)}</ul></details>
      {previous ? prior.data && !prior.isError && <ManifestDifference current={manifest.data} previous={prior.data} /> : <p>此快照没有更早的修订可比较。</p>}
      <DeliveryHistory snapshot={snapshot} manifest={manifest.data} disabled={manifest.isFetching} />
    </>}
    {canOpenDraft && revision !== undefined && <OpenDraft key={`${snapshot.id}:${revision}`} id={snapshot.id} revision={revision} onOpened={onDraft} />}
  </section>
}

const ManifestDifference = ({ current, previous }: { current: ReleaseManifest; previous: ReleaseManifest }) => {
  const oldFiles = new Map(previous.files.map(file => [file.relativePath, file]))
  const newFiles = new Map(current.files.map(file => [file.relativePath, file]))
  const paths = [...new Set([...oldFiles.keys(), ...newFiles.keys()])].sort()
  const changes = paths.flatMap(path => {
    const old = oldFiles.get(path), next = newFiles.get(path)
    return !old ? [`新增：${path}`] : !next ? [`移除：${path}`]
      : old.sha256 !== next.sha256 || old.length !== next.length ? [`变更：${path}`] : []
  })
  return <details><summary>与上一修订 {previous.sourceWorkspaceRevision} 的差异</summary>
    <p>文件变化 {changes.length} 项；节点：{JSON.stringify(previous.nodes) === JSON.stringify(current.nodes) ? '相同' : '已变化'}；
      leaf：{JSON.stringify(previous.leaves) === JSON.stringify(current.leaves) ? '相同' : '已变化'}；
      生命周期：{JSON.stringify(previous.lifecycle) === JSON.stringify(current.lifecycle) ? '相同' : '已变化'}。</p>
    <p>标准证据：{JSON.stringify(previous.validation) === JSON.stringify(current.validation) ? '相同' : '已变化'}；历史基线：{previous.historyBaselineDigest === current.historyBaselineDigest ? '相同' : '已变化'}。</p>
    <ul>{changes.map(change => <li key={change}>{change}</li>)}</ul>
    <p>完整属性与标准证据请下载两份清单核对。</p>
  </details>
}

const OpenDraft = ({ id, revision, onOpened }: { id: string; revision: number; onOpened: () => void }) => {
  const [key] = useState(() => attemptIdentity(JSON.stringify(['draft', id, revision])))
  const open = useMutation({ mutationFn: () => openFinalizedDraft(id, { expectedRevision: revision, idempotencyKey: key }), onSuccess: onOpened })
  return <div><p>创建新草稿可继续修改当前序列；历史快照及交付记录保持不变。</p><ErrorNotice error={open.error} />
    <Button disabled={open.isSuccess} loading={open.isPending} onClick={() => open.mutate()}>创建新草稿修订</Button></div>
}

const DeliveryHistory = ({ snapshot, manifest, disabled }: { snapshot: FinalizedSequence; manifest: ReleaseManifest; disabled: boolean }) => {
  const client = useQueryClient()
  const key = ['deliveries', snapshot.id]
  const records = useQuery({ queryKey: key, queryFn: ({ signal }) => loadDeliveries(snapshot.id, signal) })
  const [selected, setSelected] = useState<string>()
  const save = useMutation({ mutationFn: (data: FormData) => registerDelivery(snapshot.id, buildDeliveryBody(snapshot, manifest, {
    channel: text(data, 'channel'), recordedBy: text(data, 'recordedBy'), externalReference: text(data, 'externalReference') || null,
    note: text(data, 'note') || null,
  })), onSuccess: record => { client.setQueryData(key, (current: Awaited<ReturnType<typeof loadDeliveries>> | undefined) => [...(current ?? []), record]); void records.refetch() } })
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!disabled && !save.isPending) save.mutate(new FormData(event.currentTarget)) }
  return <section className="space-y-2" aria-label="交付记录">
    <h4 className="font-semibold">交付记录</h4><ErrorNotice error={records.error || save.error} />
    <Button size="small" onClick={() => void records.refetch()}>刷新交付记录</Button>
    {snapshot.validationMode === 'Formal' && snapshot.isReadyForFinalization
      ? <form data-testid="delivery-form" onSubmit={submit} className="space-y-2">
        <p>只登记实际完成的交付。包摘要来自此快照的 ZIP；登记人是人工声明。响应异常时先刷新记录核对，避免重复登记。</p>
        <label>渠道<input required maxLength={64} name="channel" className={inputClass} /></label>
        <label>外部编号<input maxLength={256} name="externalReference" className={inputClass} /></label>
        <label>登记人（人工声明）<input required maxLength={128} name="recordedBy" className={inputClass} /></label>
        <label>备注<textarea maxLength={2048} name="note" className={inputClass} /></label>
        <Button htmlType="submit" disabled={disabled} loading={save.isPending}>追加交付记录</Button>
      </form> : <p>开发快照不能登记交付；正式快照也必须满足就绪要求。</p>}
    {save.isSuccess && <p role="status">交付记录已追加。</p>}
    <ul>{records.data?.map(record => <li key={record.id} className="break-all border-b py-2">
      {record.channel} · {record.recordedUtc} · {record.externalReference} · {record.recordedBy}<br />ZIP SHA-256：{record.packageSha256}{' '}
      <Button size="small" onClick={() => setSelected(record.id)}>查看回执</Button>
    </li>)}</ul>
    {selected && <ReceiptHistory key={selected} deliveryId={selected} />}
  </section>
}

const ReceiptHistory = ({ deliveryId }: { deliveryId: string }) => {
  const receipts = useQuery({ queryKey: ['delivery-receipts', deliveryId], queryFn: ({ signal }) => loadDeliveryReceipts(deliveryId, signal) })
  const save = useMutation({ mutationFn: (body: RegisterReceiptBody) => registerDeliveryReceipt(deliveryId, body), onSuccess: () => { void receipts.refetch() } })
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const data = new FormData(event.currentTarget), kind = text(data, 'kind')
    if (!['0', '1', '2'].includes(kind) || save.isPending) return
    save.mutate({ kind: Number(kind) as RegisterReceiptBody['kind'], outcome: text(data, 'outcome'),
      rawText: String(data.get('rawText') ?? ''), externalReference: text(data, 'receiptReference') || null, recordedBy: text(data, 'receiptRecordedBy') })
  }
  return <section className="space-y-2 border rounded p-2" aria-label="回执记录">
    <h4>回执与原文</h4><ErrorNotice error={receipts.error || save.error} />
    <Button size="small" onClick={() => void receipts.refetch()}>刷新回执</Button>
    <p>传输、技术校验和业务结果分开记录；每次追加保留原文及摘要。响应异常时先刷新核对。</p>
    <form data-testid="receipt-form" onSubmit={submit} className="space-y-2">
      <label>回执类型<select required name="kind" defaultValue="" className={inputClass}><option value="" disabled>请选择类型</option>
        {receiptLabels.map((label, index) => <option value={index} key={index}>{label}</option>)}</select></label>
      <label>原始结果<input required name="outcome" maxLength={64} className={inputClass} /></label>
      <label>原始回执<textarea required name="rawText" maxLength={512 * 1024} className={inputClass} /></label>
      <label>外部编号<input name="receiptReference" maxLength={256} className={inputClass} /></label>
      <label>登记人（人工声明）<input required name="receiptRecordedBy" maxLength={128} className={inputClass} /></label>
      <Button htmlType="submit" loading={save.isPending}>追加回执</Button>
    </form>
    {save.isSuccess && <p role="status">回执已追加，原文与摘要已保存。</p>}
    <ul>{receipts.data?.map(receipt => <li key={receipt.id} className="break-all">
      {receiptLabels[receipt.kind] ?? '未知类型'} · {receipt.outcome} · {receipt.recordedUtc} · {receipt.recordedBy}
      <details><summary>原文与 SHA-256</summary><pre className="whitespace-pre-wrap">{receipt.rawText}</pre>{receipt.rawTextSha256}</details>
    </li>)}</ul>
  </section>
}
