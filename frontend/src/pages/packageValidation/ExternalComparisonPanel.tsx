import { useState } from 'react'
import { Alert, Button, Descriptions, Form, Input, Space, Spin, Table, Tag } from 'antd'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  loadExternalComparison, loadExternalComparisonText, registerExternalComparison,
  type ExternalComparisonDifference,
} from '../../packageValidationActions'

type Props = { runId: string; packageDigest: string }

// External evidence is expert-supplied: a tool/version, its actual report, and an
// explanation for every observed difference. The panel never infers evidence from a
// matching digest or from the presence of an installed tool.
export const ExternalComparisonPanel = ({ runId, packageDigest }: Props) => {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [report, setReport] = useState('')
  const [differences, setDifferences] = useState<ExternalComparisonDifference[]>([])
  const [difference, setDifference] = useState<ExternalComparisonDifference>({ externalRuleId: '', internalRuleId: null, externalMessage: '', explanation: '' })
  const evidence = useQuery({ queryKey: ['package-validation-external', runId], queryFn: ({ signal }) => loadExternalComparison(runId, signal) })
  const text = useQuery({
    queryKey: ['package-validation-external-text', runId], enabled: evidence.data != null,
    queryFn: ({ signal }) => loadExternalComparisonText(runId, signal),
  })
  const submit = async (values: { toolName: string; toolVersion: string }) => {
    setBusy(true); setError(undefined)
    try {
      await registerExternalComparison(runId, { ...values, packageDigest, report, differences })
      await queryClient.invalidateQueries({ queryKey: ['package-validation-external', runId] })
      setReport(''); setDifferences([])
    } catch (caught) { setError(caught instanceof Error ? caught.message : '登记外部证据失败') }
    finally { setBusy(false) }
  }
  return <Space orientation="vertical" size="middle" className="w-full">
    <Alert type="info" showIcon title="外部对照是资格证据，不是通过证明"
      description="必须登记实际工具版本、同一包摘要对应的真实报告，并逐条解释与本引擎的差异。工具已安装或包摘要相同都不构成证据。" />
    {error && <Alert type="error" showIcon title="外部证据操作失败" description={error} />}
    {evidence.isFetching && <Spin aria-label="正在读取外部对照证据" />}
    {evidence.data ? <>
      <Descriptions column={{ xs: 1, sm: 2 }} size="small" bordered className="break-all" items={[
        { key: 'tool', label: '工具 / 版本', children: `${evidence.data.toolName} ${evidence.data.toolVersion}` },
        { key: 'recorded', label: '登记时间', children: evidence.data.recordedUtc },
        { key: 'package', label: '包摘要', children: evidence.data.packageDigest },
        { key: 'report', label: '报告摘要', children: evidence.data.reportSha256 },
      ]} />
      <Table size="small" rowKey="externalRuleId" pagination={false} dataSource={evidence.data.differences} scroll={{ x: 700 }} columns={[
        { title: '外部规则', dataIndex: 'externalRuleId' },
        { title: '内部规则', render: (_, row) => row.internalRuleId || '—' },
        { title: '外部结论', dataIndex: 'externalMessage' },
        { title: '差异解释', dataIndex: 'explanation' },
      ]} />
      {text.data && <details><summary className="cursor-pointer">外部工具原始报告（只读）</summary>
        <pre className="bg-slate-50 border border-slate-200 rounded p-3 text-xs overflow-auto max-h-96" data-testid="external-report-text">{text.data}</pre>
      </details>}
    </> : !evidence.isFetching && <Tag color="orange">此运行尚无外部对照证据</Tag>}
    {!evidence.data && <Form layout="vertical" disabled={busy} onFinish={values => void submit(values as { toolName: string; toolVersion: string })}>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-3">
        <Form.Item name="toolName" label="外部工具" rules={[{ required: true, message: '请输入工具名称' }]}><Input /></Form.Item>
        <Form.Item name="toolVersion" label="工具版本" rules={[{ required: true, message: '请输入工具版本' }]}><Input /></Form.Item>
      </div>
      <Form.Item label="外部报告内容（原文）" required><Input.TextArea rows={4} value={report} onChange={event => setReport(event.target.value)} /></Form.Item>
      <Form.Item label="差异解释" required>
        <Space.Compact className="w-full">
          <Input aria-label="外部规则 ID" placeholder="外部规则 ID" value={difference.externalRuleId} onChange={event => setDifference({ ...difference, externalRuleId: event.target.value })} />
          <Input aria-label="对应内部规则 ID" placeholder="内部规则 ID（可空）" value={difference.internalRuleId || ''} onChange={event => setDifference({ ...difference, internalRuleId: event.target.value || null })} />
        </Space.Compact>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-3 mt-2">
          <Input aria-label="外部结论" placeholder="外部结论" value={difference.externalMessage} onChange={event => setDifference({ ...difference, externalMessage: event.target.value })} />
          <Input aria-label="差异解释" placeholder="差异解释" value={difference.explanation} onChange={event => setDifference({ ...difference, explanation: event.target.value })} />
        </div>
        <Button className="mt-2" onClick={() => {
          setError(undefined)
          if (!difference.externalRuleId.trim() || !difference.externalMessage.trim() || !difference.explanation.trim()) {
            setError('每条差异都需要外部规则 ID、外部结论和解释。'); return
          }
          setDifferences([...differences, difference])
          setDifference({ externalRuleId: '', internalRuleId: null, externalMessage: '', explanation: '' })
        }}>添加差异</Button>
      </Form.Item>
      {differences.length > 0 && <Table size="small" rowKey="externalRuleId" pagination={false} dataSource={differences} columns={[
        { title: '外部规则', dataIndex: 'externalRuleId' }, { title: '解释', dataIndex: 'explanation' },
        { title: '操作', width: 70, render: (_, row) => <Button size="small" onClick={() => setDifferences(differences.filter(item => item !== row))}>移除</Button> },
      ]} />}
      <div className="mt-3"><Button type="primary" htmlType="submit" loading={busy} disabled={!report.trim()}>登记外部对照证据</Button></div>
    </Form>}
  </Space>
}
