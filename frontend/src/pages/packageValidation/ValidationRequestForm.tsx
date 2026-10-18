import { useRef, useState } from 'react'
import { Alert, Button, Card, Form, Input, Select, Space, Table, Tag } from 'antd'
import type { CreatePackageValidationRequest, PackageInputDto, PackageRunSummary, ValidationProfileScope } from '../../api/generated'
import { createValidationRun } from '../../packageValidationActions'
import { InputRegistration } from './InputRegistration'

type SelectedHistory = { input: PackageInputDto; profile: string }
type Role = 'target' | 'comparison' | 'history'
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export const ValidationRequestForm = ({ profiles, onCreated }: { profiles: ValidationProfileScope[]; onCreated: (run: PackageRunSummary) => void }) => {
  const [applicationId, setApplicationId] = useState<string>(() => crypto.randomUUID())
  const [target, setTarget] = useState<PackageInputDto>()
  const [comparison, setComparison] = useState<PackageInputDto>()
  const [history, setHistory] = useState<SelectedHistory[]>([])
  const [profile, setProfile] = useState<string>()
  const [role, setRole] = useState<Role>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const attempt = useRef<{ signature: string; key: string } | null>(null)
  const options = profiles.map(item => ({ value: item.profileSnapshotId, label: item.profileSnapshotId }))
  const selectedProfile = profiles.find(item => item.profileSnapshotId === profile)

  const accept = (input: PackageInputDto) => {
    setRole(undefined)
    setError(undefined)
    if (role === 'target') { setTarget(input); setComparison(undefined); setHistory([]) }
    if (role === 'comparison') {
      if (input.sequenceNumber !== target?.sequenceNumber || input.sourceKind === target.sourceKind || input.limitsDigest !== target.limitsDigest) {
        setError('对照输入必须为同序列、相同限额的另一种形式（目录与 ZIP）。'); return
      }
      setComparison(input)
    }
    if (role === 'history') {
      if (!target || input.sequenceNumber >= target.sequenceNumber || input.limitsDigest !== target.limitsDigest) {
        setError('历史必须早于目标序列，且读取限额相同。'); return
      }
      if (history.some(item => item.input.sequenceNumber === input.sequenceNumber)) {
        setError('每个历史序列只能选一个来源版本；请先移除旧选择。'); return
      }
      setHistory([...history, { input, profile: '' }].sort((a, b) => a.input.sequenceNumber.localeCompare(b.input.sequenceNumber)))
    }
  }
  const submit = async () => {
    if (busy) return
    setError(undefined)
    if (!target || !profile || history.some(item => !item.profile)) { setError('请选择目标输入、目标规则范围以及每个历史输入的规则范围。'); return }
    const body: Omit<CreatePackageValidationRequest, 'idempotencyKey'> = {
      inputId: target.id, comparisonInputId: comparison?.id ?? null, profileSnapshotId: profile,
      mode: 'Development', history: history.map(item => ({ inputId: item.input.id, profileSnapshotId: item.profile })),
    }
    const signature = JSON.stringify(body)
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: crypto.randomUUID() }
    setBusy(true)
    try {
      const run = await createValidationRun({ ...body, idempotencyKey: attempt.current.key })
      attempt.current = null
      onCreated(run)
    } catch (caught) { setError(caught instanceof Error ? caught.message : '创建任务失败；重试将使用相同幂等标识。') }
    finally { setBusy(false) }
  }
  const inputDescription = (input: PackageInputDto) => <div className="break-all text-xs space-y-1">
    <div><Tag>{input.sequenceNumber}</Tag>{input.sourceKind} · {input.fileCount} 文件 · {input.totalBytes.toLocaleString()} 字节</div>
    <div>输入 ID：{input.id}</div><div>SHA-256：{input.inputDigest}</div>
  </div>

  return <Card title="选择已有申报包" size="small">
    <Form layout="vertical" disabled={busy}>
      <Form.Item label="申请标识（外部包分组 UUID，不要求业务申请记录）" htmlFor="validation-application">
        <Space.Compact className="w-full"><Input id="validation-application" value={applicationId} disabled={!!target} onChange={event => setApplicationId(event.target.value)} />
          <Button onClick={() => { setApplicationId(crypto.randomUUID()); setTarget(undefined); setComparison(undefined); setHistory([]); attempt.current = null }}>新建分组</Button></Space.Compact>
      </Form.Item>
      <Space wrap className="mb-3">
        <Button disabled={!uuidPattern.test(applicationId)} onClick={() => setRole('target')}>{target ? '更换目标输入' : '选择目标输入'}</Button>
        <Button disabled={!target} onClick={() => setRole('comparison')}>选择目录 / ZIP 对照</Button>
        <Button disabled={!target} onClick={() => setRole('history')}>添加历史序列</Button>
      </Space>
      {target && <div className="rounded border border-slate-200 p-3 mb-3" data-testid="selected-validation-target">{inputDescription(target)}</div>}
      {comparison && <div className="rounded border border-slate-200 p-3 mb-3">对照输入 {inputDescription(comparison)}<Button size="small" onClick={() => setComparison(undefined)}>移除对照</Button></div>}
      <Form.Item label="目标规则范围（显式选择）" htmlFor="validation-profile" required>
        <Select id="validation-profile" value={profile} onChange={setProfile} options={options} placeholder="选择规则与标准快照" />
      </Form.Item>
      {selectedProfile && <Alert type="warning" showIcon title={selectedProfile.qualifiedForFinalization ? '开发校验模式' : '此规则范围尚未取得正式定稿资格'} description={`${selectedProfile.description} ${selectedProfile.qualificationReason}`} className="mb-3" />}
      <h3 className="font-semibold">显式历史基线（{history.length}）</h3>
      <p className="text-slate-500 text-sm">没有选择的历史不会自动发现；导入来源一律保留 Unverified，不宣称已递交或已核实。</p>
      <Table size="small" rowKey={item => item.input.id} pagination={false} dataSource={history} scroll={{ x: 620 }} columns={[
        { title: '历史输入', render: (_, item) => inputDescription(item.input) },
        { title: '历史规则范围', width: 260, render: (_, item) => <Select aria-label={`历史 ${item.input.sequenceNumber} 规则范围`} className="w-full" value={item.profile || undefined} options={options} onChange={value => setHistory(history.map(row => row === item ? { ...row, profile: value } : row))} /> },
        { title: '操作', width: 70, render: (_, item) => <Button size="small" onClick={() => setHistory(history.filter(row => row !== item))}>移除</Button> },
      ]} />
      {error && <Alert type="error" showIcon title="请求未完成" description={error} className="mt-3" />}
      <Button type="primary" className="mt-4" loading={busy} disabled={!target || !profile || history.some(item => !item.profile)} onClick={() => void submit()}>开始独立校验</Button>
    </Form>
    {role && <InputRegistration applicationId={applicationId} title={{ target: '选择目标输入', comparison: '选择对照输入', history: '选择历史输入' }[role]} onRegistered={accept} onClose={() => setRole(undefined)} />}
  </Card>
}
