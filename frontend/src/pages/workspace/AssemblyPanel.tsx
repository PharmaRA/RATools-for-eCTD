import { useRef, useState } from 'react'
import { Alert, Button, Card, Input, Space, Table, Tag } from 'antd'
import { apiFetch, buildJsonRequestInit } from '../../apiClient'
import type { AssemblyPlan, AssemblyExecutionResult as ExecutionResult } from '../../api/generated'

const planUrl = (appId: string, seq: string) =>
  `/api/applications/${encodeURIComponent(appId)}/sequences/${encodeURIComponent(seq)}/assembly-plans`

// Bulk assembly is preview-first. The panel never executes on behalf of the operator:
// the returned plan must be reviewed and the exact digest is what execution submits.
type Props = {
  appId: string; seqNumber: string; workspaceRevision?: number; disabled: boolean; onExecuted: () => Promise<void> | void
}
export const AssemblyPanel = (props: Props) => <AssemblyEditor
  key={JSON.stringify([props.appId, props.seqNumber, props.workspaceRevision, props.disabled])} {...props} />

const AssemblyEditor = ({ appId, seqNumber, workspaceRevision, disabled, onExecuted }: Props) => {
  const [manifest, setManifest] = useState('')
  const [plan, setPlan] = useState<AssemblyPlan>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [executed, setExecuted] = useState<ExecutionResult>()
  const inputRevision = useRef(0)
  const executable = !disabled && !busy && plan && plan.invalidRows === 0 && plan.validRows > 0 && plan.planDigest.length > 0
  const preview = async () => {
    if (disabled || busy) return
    if (workspaceRevision === undefined) { setError('工作区修订尚未加载，暂不能预览装配计划。'); return }
    setBusy(true); setError(undefined); setPlan(undefined); setExecuted(undefined)
    const requestedRevision = inputRevision.current
    try {
      const result = await apiFetch(planUrl(appId, seqNumber), buildJsonRequestInit('POST',
        { expectedRevision: workspaceRevision, manifest })) as AssemblyPlan
      if (requestedRevision === inputRevision.current) setPlan(result)
    } catch (caught) { if (requestedRevision === inputRevision.current) setError(caught instanceof Error ? caught.message : '生成装配预览失败') }
    finally { setBusy(false) }
  }
  const execute = async () => {
    if (!executable || !plan) return
    setBusy(true); setError(undefined)
    try {
      const result = await apiFetch('/api/assembly-plans/execute', buildJsonRequestInit('POST', {
        applicationId: appId, sequenceNumber: seqNumber, expectedRevision: plan.expectedRevision,
        planDigest: plan.planDigest, manifest,
      })) as ExecutionResult
      setExecuted(result)
      setPlan(undefined)
      await onExecuted()
    } catch (caught) { setError(caught instanceof Error ? caught.message : '执行装配计划失败') }
    finally { setBusy(false) }
  }
  return <Card size="small" title="批量装配（CSV 预览）" className="mb-3">
    <Space orientation="vertical" size="middle" className="w-full">
      <Alert type="info" showIcon title="先预览，再执行"
        description="每行必须给出 documentId、具体节点实例和操作；替换/追加/删除需给出更早序列的 placement ID。无效行会逐行报告，不会被跳过；执行只会提交当前预览摘要对应的有效行。" />
      {error && <Alert type="error" showIcon title="装配操作失败" description={error} />}
      {executed && <Alert type="success" showIcon title="装配已提交"
        description={`工作区修订已更新为 ${executed.workspaceRevision}，创建 ${executed.placementIds.length} 个挂载。`} />}
      <Input.TextArea aria-label="装配清单 CSV" rows={5} value={manifest} onChange={event => {
        inputRevision.current++; setManifest(event.target.value); setPlan(undefined); setExecuted(undefined); setError(undefined)
      }}
        placeholder={'documentId,nodeInstanceId,operation,title,sortOrder\n<doc-id>,<node-id>,new,Specification,0'} />
      <Space>
        <Button type="primary" loading={busy} disabled={disabled || !manifest.trim()} onClick={() => void preview()}>生成装配预览</Button>
        <Button loading={busy} disabled={!executable} onClick={() => void execute()}>执行已预览计划</Button>
      </Space>
      {plan && <>
        <Space wrap size="small">
          <Tag>总行数 {plan.totalRows}</Tag><Tag color="green">有效 {plan.validRows}</Tag>
          <Tag color={plan.invalidRows ? 'red' : 'default'}>无效 {plan.invalidRows}</Tag>
          <Tag>计划摘要 {plan.planDigest.slice(0, 12)}…</Tag>
        </Space>
        {plan.diagnostics.length > 0 && <Alert type="warning" showIcon title="清单级诊断"
          description={plan.diagnostics.map(item => item.message).join('；')} />}
        <Table size="small" rowKey="lineNumber" dataSource={plan.rows} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 780 }}
          columns={[
            { title: '行', dataIndex: 'lineNumber', width: 60 },
            { title: '状态', width: 90, render: (_, row) => <Tag color={row.status === 0 ? 'green' : 'red'}>
              {row.status === 0 ? '有效' : '无效'}</Tag> },
            { title: '操作 / 标题', render: (_, row) => <span className="break-all">{row.operation}{row.title ? ` · ${row.title}` : ''}</span> },
            { title: '文档 / 节点', render: (_, row) => <span className="break-all text-xs">
              {row.documentId || '无文档'}<br />{row.nodeInstanceId || '无节点'}</span> },
            { title: '历史目标', render: (_, row) => <span className="break-all text-xs">{row.targetLeafAddress || '—'}</span> },
            { title: '诊断', render: (_, row) => <span className="text-xs">{row.diagnostics.map(item => item.message).join('；') || '—'}</span> },
          ]} />
      </>}
    </Space>
  </Card>
}
