import { useEffect, useRef, useState } from 'react'
import { ApiRequestError, apiFetch, buildJsonRequestInit } from '../../apiClient'
import type { RegionalFieldDescriptor, RegionalMetadataPreflightDto, RegionalMetadataPreviewDto,
  RegionalMetadataRevisionDto, RegionalMetadataSchemaDto, SequencePublishingMetadataDto } from '../../api/generated'
import { RegionalSchemaFields, type RegionalSelection } from './RegionalSchemaFields'
import { regionalFieldPath, withRegionalValue, type RegionalValuePath } from './regionalMetadataValues'
type Props = { appId: string; seqNumber: string; workspaceRevision?: number; disabled: boolean;
  nodes: readonly RegionalSelection[]; placements: readonly RegionalSelection[]; onChanged: () => void | Promise<void> }
type Draft = Record<string, unknown>
type Snapshot = { shared: SequencePublishingMetadataDto; schema: RegionalMetadataSchemaDto; revision?: RegionalMetadataRevisionDto;
  defaults?: RegionalMetadataRevisionDto; defaultsSchema: RegionalMetadataSchemaDto; expectedRevision: number }
type Preview = { kind: 'inheritance' | 'node-context-copy'; result: RegionalMetadataPreviewDto; request: Record<string, unknown> }
const asDraft = (value: unknown): Draft => value as Draft
const emptyFields = (fields: readonly RegionalFieldDescriptor[]): Draft => Object.fromEntries(fields.map(field =>
  [field.key, field.kind === 'collection' ? [] : field.kind === 'object' ? emptyFields(field.fields) : null]))
const emptyContent = (schema: RegionalMetadataSchemaDto, fields: readonly RegionalFieldDescriptor[]) =>
  ({ ...emptyFields(fields), schemaVersion: schema.schemaVersion, regionKey: schema.regionKey })
const errorText = (error: unknown) => error instanceof Error ? error.message : '区域元数据操作失败'
const regionalRequest = async <T,>(...args: Parameters<typeof apiFetch>): Promise<T> => apiFetch(...args) as Promise<T>

export function RegionalMetadataPanel(props: Props) {
  return <RegionalEditor key={JSON.stringify([props.appId, props.seqNumber])} {...props} />
}
function RegionalEditor({ appId, seqNumber, workspaceRevision, disabled, nodes, placements, onChanged }: Props) {
  const sequenceBase = `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seqNumber)}/publishing-metadata`
  const appBase = `/api/applications/${encodeURIComponent(appId)}/regional-metadata`
  const [snapshot, setSnapshot] = useState<Snapshot>()
  const [draft, setDraft] = useState<Draft>({})
  const [defaultsDraft, setDefaultsDraft] = useState<Draft>({})
  const [dirty, setDirty] = useState(false), [defaultsDirty, setDefaultsDirty] = useState(false)
  const [edited, setEdited] = useState<Set<string>>(new Set()), [defaultsEdited, setDefaultsEdited] = useState<Set<string>>(new Set())
  const [tab, setTab] = useState<'sequence' | 'defaults'>('sequence')
  const [busy, setBusy] = useState(false), [error, setError] = useState<string>(), [status, setStatus] = useState<string>()
  const [preflight, setPreflight] = useState<RegionalMetadataPreflightDto>()
  const [preview, setPreview] = useState<Preview>()
  const [sourceId, setSourceId] = useState(''), [sourceNode, setSourceNode] = useState(''), [targetIds, setTargetIds] = useState<string[]>([])
  const [historyId, setHistoryId] = useState(''), [history, setHistory] = useState<RegionalMetadataRevisionDto>()
  const container = useRef<HTMLElement>(null)
  const alive = useRef(true)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const stale = !!snapshot && workspaceRevision !== snapshot.expectedRevision
  const readOnly = disabled || busy
  const canMutate = !!snapshot && !readOnly && !stale && Number.isSafeInteger(snapshot.expectedRevision)
  const changed = (path: RegionalValuePath, value: unknown) => {
    setPreview(undefined); setPreflight(undefined); setStatus(undefined)
    if (tab === 'sequence') {
      setDraft(current => withRegionalValue(current, path, value)); setDirty(true)
      setEdited(current => new Set([...current, regionalFieldPath(path)]))
    } else {
      setDefaultsDraft(current => withRegionalValue(current, path, value)); setDefaultsDirty(true)
      setDefaultsEdited(current => new Set([...current, regionalFieldPath(path)]))
    }
  }
  const run = async (operation: () => Promise<void>) => {
    if (busy) return
    setBusy(true); setError(undefined); setStatus(undefined)
    try { await operation() } catch (caught) { if (alive.current) setError(errorText(caught)) }
    finally { if (alive.current) setBusy(false) }
  }
  const acceptRevision = async (revision: RegionalMetadataRevisionDto, shared?: SequencePublishingMetadataDto,
    capturedSchema?: RegionalMetadataSchemaDto) => {
    const expectedSchema = capturedSchema ?? snapshot?.schema
    if (revision.applicationId !== appId || revision.sequenceNumber !== seqNumber || !revision.content
      || !Number.isSafeInteger(revision.workspaceRevision) || revision.standardsSnapshotId !== expectedSchema?.standardsSnapshotId)
      throw new Error('返回的区域修订身份不匹配，请保留输入并重新读取。')
    if (!alive.current) return
    setSnapshot(current => current && ({ ...current, schema: capturedSchema ?? current.schema, revision, expectedRevision: revision.workspaceRevision!,
      shared: { ...(shared ?? current.shared), regional: revision, workspaceRevision: revision.workspaceRevision! } }))
    setDraft({ ...asDraft(revision.content), nodeContexts: revision.nodeContexts, documentContexts: revision.documentContexts })
    setDirty(false); setEdited(new Set()); setPreview(undefined); setPreflight(undefined); setStatus('区域草稿已保存。')
    await onChanged()
  }
  const load = () => run(async () => {
    const shared = await regionalRequest<SequencePublishingMetadataDto>(sequenceBase)
    if (shared.applicationId !== appId || shared.sequenceNumber !== seqNumber || !Number.isSafeInteger(shared.workspaceRevision))
      throw new Error('共享元数据不属于当前工作区。')
    const defaults = await regionalRequest<RegionalMetadataRevisionDto | null>(`${appBase}/defaults`).catch((caught: unknown) => {
        if (caught instanceof ApiRequestError && caught.status === 404) return null
        throw caught
      })
    if (defaults && (defaults.applicationId !== appId || defaults.sequenceNumber !== null)) throw new Error('申请默认值归属不匹配。')
    const selectedSnapshot = shared.regional?.standardsSnapshotId ?? defaults?.standardsSnapshotId
    const query = selectedSnapshot ? `?standardsSnapshotId=${encodeURIComponent(selectedSnapshot)}` : ''
    const schema = await regionalRequest<RegionalMetadataSchemaDto>(`${appBase}/schema${query}`)
    const defaultsSnapshot = defaults?.standardsSnapshotId ?? schema.standardsSnapshotId
    const defaultsQuery = defaultsSnapshot === schema.standardsSnapshotId ? query
      : `?standardsSnapshotId=${encodeURIComponent(defaultsSnapshot)}`
    const defaultsSchema = await regionalRequest<RegionalMetadataSchemaDto>(`${appBase}/schema${defaultsQuery}`)
    if (schema.regionKey !== 'EU' && schema.regionKey !== 'US') throw new Error('当前区域 schema 不受支持。')
    if (selectedSnapshot && schema.standardsSnapshotId !== selectedSnapshot) throw new Error('区域 schema 不属于所选标准快照。')
    if (defaultsSchema.standardsSnapshotId !== defaultsSnapshot || defaultsSchema.regionKey !== schema.regionKey)
      throw new Error('申请默认值 schema 不属于其标准快照。')
    if (shared.regional && (shared.regional.applicationId !== appId || shared.regional.sequenceNumber !== seqNumber
      || shared.regional.standardsSnapshotId !== schema.standardsSnapshotId)) throw new Error('区域元数据归属或标准快照不匹配。')
    if (!alive.current) return
    setSnapshot({ shared, schema, defaultsSchema, revision: shared.regional ?? undefined, defaults: defaults ?? undefined, expectedRevision: shared.workspaceRevision })
    setDraft({ ...asDraft(shared.regional?.content ?? emptyContent(schema, schema.sequenceFields)),
      nodeContexts: shared.regional?.nodeContexts ?? [], documentContexts: shared.regional?.documentContexts ?? [] })
    setDefaultsDraft(asDraft(defaults?.defaults ?? emptyContent(defaultsSchema, defaultsSchema.defaultsFields)))
    setDirty(false); setDefaultsDirty(false); setEdited(new Set()); setDefaultsEdited(new Set()); setPreview(undefined); setPreflight(undefined)
    setSourceId(shared.regional?.id ?? ''); setHistory(undefined)
  })
  const sequenceRequest = () => {
    if (!snapshot) throw new Error('请先读取区域元数据。')
    const { nodeContexts, documentContexts, ...content } = draft
    return { standardsSnapshotId: snapshot.schema.standardsSnapshotId, content, nodeContexts, documentContexts }
  }
  const defaultsRequest = () => ({ expectedDefaultsRevision: snapshot?.defaults?.metadataRevision ?? 0,
    standardsSnapshotId: snapshot!.defaultsSchema.standardsSnapshotId, defaults: defaultsDraft })
  const save = () => run(async () => {
    if (!snapshot || !canMutate) return
    if (tab === 'defaults') {
      const saved = await regionalRequest<RegionalMetadataRevisionDto>(`${appBase}/defaults`, buildJsonRequestInit('PUT', defaultsRequest()))
      if (saved.applicationId !== appId || saved.sequenceNumber !== null || !saved.defaults
        || saved.standardsSnapshotId !== snapshot.defaultsSchema.standardsSnapshotId) throw new Error('返回的申请默认值身份不匹配。')
      if (!alive.current) return
      setSnapshot(current => current && ({ ...current, defaults: saved })); setDefaultsDraft(asDraft(saved.defaults))
      setDefaultsDirty(false); setDefaultsEdited(new Set()); setPreflight(undefined); setStatus('申请默认值已保存；已有序列没有自动改变。')
      return
    }
    const request: Record<string, unknown> = { expectedRevision: snapshot.expectedRevision, regional: sequenceRequest() }
    for (const field of ['applicationType', 'submissionType', 'submissionSubtype', 'sequenceDescription', 'applicantName', 'formType',
      'applicantContactName', 'applicantContactType', 'telephone', 'telephoneNumberType', 'email'] as const) request[field] = snapshot.shared[field] ?? null
    const saved: SequencePublishingMetadataDto = await apiFetch(sequenceBase, buildJsonRequestInit('PUT', request))
    if (!saved.regional || saved.workspaceRevision !== saved.regional.workspaceRevision) throw new Error('保存响应缺少一致的区域修订，请重新读取。')
    await acceptRevision(saved.regional, saved)
  })
  const check = () => run(async () => {
    if (!snapshot) return
    const result = await regionalRequest<RegionalMetadataPreflightDto>(tab === 'defaults' ? `${appBase}/defaults/preflight` : `${sequenceBase}/regional/preflight`,
      buildJsonRequestInit('POST', tab === 'defaults' ? defaultsRequest() : sequenceRequest()))
    const expectedSnapshot = tab === 'defaults' ? snapshot.defaultsSchema.standardsSnapshotId : snapshot.schema.standardsSnapshotId
    if (result.standardsSnapshotId !== expectedSnapshot) throw new Error('预检使用了其他标准快照。')
    if (alive.current) { setPreflight(result); setPreview(undefined) }
  })
  const makePreview = (kind: Preview['kind']) => run(async () => {
    if (!snapshot || !canMutate || dirty) return
    const request = kind === 'inheritance' ? { expectedRevision: snapshot.expectedRevision, defaultsRevisionId: snapshot.defaults!.id }
      : { expectedRevision: snapshot.expectedRevision, sourceRevisionId: sourceId, nodeInstanceId: sourceNode, placementIds: targetIds }
    const result: RegionalMetadataPreviewDto = await apiFetch(`${sequenceBase}/regional/${kind}/preview`, buildJsonRequestInit('POST', request))
    const expectedSource = kind === 'inheritance' ? snapshot.defaults!.id : sourceId
    if (result.sourceRevisionId !== expectedSource || result.expectedRevision !== snapshot.expectedRevision || !result.sourceDigest)
      throw new Error('差异预览来源或工作区修订不匹配。')
    if (alive.current) { setPreview({ kind, request, result }); setPreflight(undefined) }
  })
  const adopt = () => run(async () => {
    if (!preview || !canMutate || dirty) return
    const revision = await regionalRequest<RegionalMetadataRevisionDto>(`${sequenceBase}/regional/${preview.kind}`, buildJsonRequestInit('POST', preview.request))
    await acceptRevision(revision)
  })
  const upgrade = () => run(async () => {
    if (!canMutate || dirty) return
    const revision = await regionalRequest<RegionalMetadataRevisionDto>(`${sequenceBase}/regional/legacy-upgrade`,
      buildJsonRequestInit('POST', { expectedRevision: snapshot!.expectedRevision }))
    const capturedSchema = await regionalRequest<RegionalMetadataSchemaDto>(`${appBase}/schema?standardsSnapshotId=${encodeURIComponent(revision.standardsSnapshotId)}`)
    if (capturedSchema.standardsSnapshotId !== revision.standardsSnapshotId || capturedSchema.regionKey !== snapshot!.schema.regionKey)
      throw new Error('升级返回的区域 schema 身份不匹配，请保留输入并重新读取。')
    await acceptRevision(revision, undefined, capturedSchema)
  })
  const locate = (path: string) => {
    const target = Array.from(container.current?.querySelectorAll<HTMLElement>('[data-field-path]') ?? [])
      .find(item => item.dataset.fieldPath === path)
    const control = target?.querySelector<HTMLElement>('input,select,button') ?? target
    control?.focus(); control?.scrollIntoView?.({ block: 'center' })
  }
  const contextCollection = (key: string, label: string, identityField: string, fields: RegionalFieldDescriptor[]): RegionalFieldDescriptor =>
    ({ key, label, kind: 'collection', required: false, options: [], fields, identityField, conditions: [], source: null, businessEvidence: null })
  const fields = snapshot && (tab === 'defaults' ? snapshot.defaultsSchema.defaultsFields : [...snapshot.schema.sequenceFields,
    contextCollection('nodeContexts', '节点区域上下文', 'nodeInstanceId', snapshot.schema.nodeContextFields),
    contextCollection('documentContexts', '文档区域上下文', 'placementId', snapshot.schema.documentContextFields)])
  return <section ref={container} className="border rounded bg-white p-4" aria-label="区域元数据编辑">
    <h3 className="font-semibold">区域元数据</h3>
    <button type="button" disabled={busy || dirty || defaultsDirty} onClick={() => void load()}>读取区域元数据</button>
    {(dirty || defaultsDirty) && <button type="button" disabled={busy} onClick={() => void load()}>放弃未保存输入并重新读取</button>}
    {busy && <p role="status">正在处理区域元数据…</p>}
    {error && <p role="alert" className="text-red-700">{error}</p>}
    {status && <p role="status">{status}</p>}
    {stale && <p role="alert">工作区修订已变化；未保存输入已保留。重新读取后再保存。</p>}
    {snapshot && fields && <>
      <p className="text-sm">区域 {snapshot.schema.regionKey} · 标准快照 <code>{(tab === 'defaults' ? snapshot.defaultsSchema : snapshot.schema).standardsSnapshotId}</code></p>
      <p className="text-amber-700">区域资格待完成；填写或保存字段不会激活正式发布资格。</p>
      <div className="flex gap-3 my-2"><button type="button" aria-pressed={tab === 'sequence'} disabled={busy} onClick={() => { setTab('sequence'); setPreflight(undefined); setPreview(undefined) }}>序列区域字段</button>
        <button type="button" aria-pressed={tab === 'defaults'} disabled={busy} onClick={() => { setTab('defaults'); setPreflight(undefined); setPreview(undefined) }}>申请可复用默认值</button></div>
      <RegionalSchemaFields fields={fields} value={tab === 'sequence' ? draft : defaultsDraft} disabled={readOnly || stale}
        nodes={nodes} placements={placements} scope={`${appId}-${seqNumber}-${tab}`} onChange={changed}
        provenance={(tab === 'sequence' ? snapshot.revision : snapshot.defaults)?.provenance ?? []} editedPaths={tab === 'sequence' ? edited : defaultsEdited} />
      <div className="flex gap-3 my-3"><button type="button" disabled={busy} onClick={() => void check()}>预检当前输入</button>
        <button type="button" disabled={!canMutate} onClick={() => void save()}>{tab === 'defaults' ? '保存申请默认值' : '保存区域草稿'}</button></div>
      {tab === 'sequence' && <>
        {snapshot.defaults && <p>可继承申请默认值修订 {snapshot.defaults.metadataRevision} · {snapshot.defaults.id}</p>}
        <button type="button" disabled={!canMutate || dirty || !snapshot.defaults || defaultsDirty} onClick={() => void makePreview('inheritance')}>预览申请默认值继承</button>
        {dirty && <p>请先保存或放弃序列输入，再预览继承或节点复制。</p>}
        {!snapshot.revision && <button type="button" disabled={!canMutate || dirty} onClick={() => void upgrade()}>从旧共享字段建立待确认记录</button>}
        {snapshot.revision && <details className="my-3"><summary>明确复制节点上下文</summary>
          <label className="block">来源区域修订 ID<input aria-label="节点上下文来源修订" disabled={readOnly} value={sourceId} onChange={event => { setSourceId(event.target.value); setPreview(undefined) }} /></label>
          <label className="block">来源节点<select aria-label="节点上下文来源节点" disabled={readOnly} value={sourceNode} onChange={event => { setSourceNode(event.target.value); setTargetIds([]); setPreview(undefined) }}>
            <option value="">请选择</option>{nodes.map(node => <option key={node.id} value={node.id}>{node.label}</option>)}</select></label>
          <fieldset><legend>目标文档放置项</legend>{placements.map(placement => <label key={placement.id} className="block"><input type="checkbox" disabled={readOnly || placement.nodeInstanceId !== sourceNode}
            checked={targetIds.includes(placement.id)} onChange={event => { setTargetIds(current => event.target.checked ? [...current, placement.id] : current.filter(id => id !== placement.id)); setPreview(undefined) }} />{placement.label}</label>)}</fieldset>
          <button type="button" disabled={!canMutate || dirty || !sourceId || !sourceNode || targetIds.length === 0} onClick={() => void makePreview('node-context-copy')}>预览节点上下文复制</button>
        </details>}
      </>}
      {preview && <div className="border p-3 my-3" aria-label="区域元数据差异预览">
        <p>来源修订 {preview.result.sourceRevisionId} · 摘要 {preview.result.sourceDigest}</p>
        <table className="w-full text-sm"><thead><tr><th>字段</th><th>原值</th><th>采用后</th></tr></thead><tbody>
          {preview.result.fieldDiffs.map(diff => <tr key={diff.fieldPath}><td>{diff.fieldPath}</td><td>{diff.before ?? '空'}</td><td>{diff.after ?? '空'}</td></tr>)}</tbody></table>
        {preview.result.fieldDiffs.length === 0 && <p>字段值没有差异；采用后仍会记录明确来源。</p>}
        <button type="button" disabled={!canMutate || dirty} onClick={() => void adopt()}>确认采用以上差异</button>
      </div>}
      {preflight && <div className="my-3" aria-label="区域元数据预检">
        <h4>字段预检与预期输出摘要</h4>
        {preflight.diagnostics.map((issue, index) => <p key={index}><button type="button" onClick={() => locate(issue.fieldPath)}>{issue.fieldPath}</button>：{issue.message}
          {issue.source && <small> · {issue.source.resourceKey} · {issue.source.locator}</small>}</p>)}
        <table className="w-full text-sm"><thead><tr><th>字段</th><th>当前输入</th></tr></thead><tbody>{preflight.summary.map((entry, index) => <tr key={index}>
          <td>{entry.fieldPath}</td><td>{entry.value ?? '空'}</td></tr>)}</tbody></table>
      </div>}
      {!preflight && (tab === 'sequence' ? snapshot.revision : snapshot.defaults)?.diagnostics.map((issue, index) => <p key={index}>
        <button type="button" onClick={() => locate(issue.fieldPath)}>{issue.fieldPath}</button>：{issue.message}</p>)}
      <details className="my-3"><summary>读取历史区域修订</summary>
        <input aria-label="历史区域修订 ID" value={historyId} disabled={busy} onChange={event => setHistoryId(event.target.value)} />
        <button type="button" disabled={busy || !historyId} onClick={() => void run(async () => {
          const result = await regionalRequest<RegionalMetadataRevisionDto>(`${appBase}/revisions/${encodeURIComponent(historyId)}${tab === 'sequence' ? `?sequenceNumber=${encodeURIComponent(seqNumber)}` : ''}`)
          if (result.applicationId !== appId || result.sequenceNumber !== (tab === 'sequence' ? seqNumber : null)) throw new Error('历史修订归属不匹配。')
          if (alive.current) setHistory(result)
        })}>读取历史修订</button>
        {history && <><p>历史标准 {history.standardsSnapshotId} · 修订 {history.metadataRevision} · {history.digest}</p><pre className="overflow-auto text-xs">{JSON.stringify(history, null, 2)}</pre></>}
      </details>
    </>}
  </section>
}
