import { useState } from 'react'
import { Alert, Button, Card, Descriptions, Form, Input, Select, Space, Table, Tabs, Tag } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../apiClient'
import type { ValidationFinding, ValidationReport } from '../../api/generated'
import { cancelValidationRun, downloadValidationJson, isValidationActive, loadValidationEvidence,
  loadValidationFindings, loadValidationRun, runUrl, type FindingFilters } from '../../packageValidationActions'
import { ExternalComparisonPanel } from './ExternalComparisonPanel'
import { FindingLocation } from './FindingLocation'
import { checkLabels, runStatusLabels, severityLabels, stageLabels } from './validationDisplay'

export const ValidationRunDetail = ({ id }: { id: string }) => {
  const queryClient = useQueryClient()
  const [filters, setFilters] = useState<FindingFilters>({})
  const [offset, setOffset] = useState(0)
  const [tab, setTab] = useState('findings')
  const [finding, setFinding] = useState<ValidationFinding>()
  const [error, setError] = useState<string>()
  const [downloading, setDownloading] = useState(false)
  const [form] = Form.useForm<FindingFilters>()
  const runQuery = useQuery({ queryKey: ['package-validation-run', id], queryFn: ({ signal }) => loadValidationRun(id, signal),
    refetchInterval: query => query.state.error ? false : isValidationActive(query.state.data) ? 1000 : false,
  })
  const run = runQuery.data
  const active = isValidationActive(run)
  const evidence = useQuery({ queryKey: ['package-validation-evidence', id], queryFn: ({ signal }) => loadValidationEvidence(id, signal), enabled: !!run })
  const findings = useQuery({ queryKey: ['package-validation-findings', id, offset, filters],
    queryFn: ({ signal }) => loadValidationFindings(id, offset, filters, signal), enabled: run?.status === 'Completed',
  })
  const report = useQuery<ValidationReport>({ queryKey: ['package-validation-report', id],
    queryFn: ({ signal }) => apiFetch(`${runUrl(id)}/report`, { signal }), enabled: run?.reportAvailable === true && tab === 'coverage',
  })
  const cancel = useMutation({ mutationFn: () => cancelValidationRun(id), onSuccess: result => {
    queryClient.setQueryData(['package-validation-run', id], result)
    void queryClient.invalidateQueries({ queryKey: ['package-validation-runs'] })
  } })
  const download = async (kind: 'report' | 'evidence' | 'manifest', inputId?: string) => {
    setDownloading(true); setError(undefined)
    try { await downloadValidationJson(inputId || id, kind) }
    catch (caught) { setError(caught instanceof Error ? caught.message : '下载失败') }
    finally { setDownloading(false) }
  }
  const errors = [runQuery.error, evidence.error, findings.error, report.error, cancel.error].filter(Boolean)
  const rules = evidence.data?.ruleCatalog.rules || []
  const categories = [...new Set(rules.map(rule => rule.category))]
  const summary = run?.coverageSummary
  return <Card size="small" title="验包运行与证据" extra={<Button loading={runQuery.isFetching} onClick={() => void runQuery.refetch()}>刷新状态</Button>}>
    <Space orientation="vertical" size="middle" className="w-full">
      {!!errors.length && <Alert type="error" showIcon title="读取或操作失败" description={errors.map(item => item instanceof Error ? item.message : '未知错误').join('；')} />}
      {run && <>
        <Alert type={run.status === 'Completed' ? 'info' : active ? 'info' : 'warning'} showIcon
          title={runStatusLabels[run.status]} description={run.failureMessage || '运行完成只表示已完成检查，不表示规则全部通过、已定稿或已获监管接收。'} />
        <Descriptions column={{ xs: 1, sm: 2 }} size="small" className="break-all" items={[
          { key: 'id', label: '运行 ID', children: run.id }, { key: 'app', label: '申请标识 / 序列', children: `${run.applicationId} / ${run.sequenceNumber}` },
          { key: 'stage', label: '阶段 / 尝试', children: `${stageLabels[run.stage] || runStatusLabels[run.status]} / ${run.attemptCount}` },
          { key: 'created', label: '创建时间', children: run.createdUtc },
          ...Object.entries(run.binding).map(([key, value]) => ({ key, label: key, children: value })),
        ]} />
        {active && <Space><Button danger loading={cancel.isPending} disabled={run.cancellationRequested} onClick={() => cancel.mutate()}>请求取消</Button>
          {run.cancellationRequested && <span role="status">已请求取消，等待后台确认终态。</span>}</Space>}
        {summary ? <div className="flex flex-wrap gap-2" aria-label="规则覆盖统计">
          <Tag>总规则 {summary.totalRules}</Tag><Tag color="green">通过 {summary.passed}</Tag><Tag color="red">失败 {summary.failed}</Tag>
          <Tag color="orange">未验证 {summary.notEvaluated}</Tag><Tag>不适用 {summary.notApplicable}</Tag><Tag color="red">阻断 {summary.blockingRules}</Tag>
        </div> : <p>此运行尚无覆盖统计，或属于没有摘要的旧记录；不补写成功。</p>}
        <Space wrap>
          <Button disabled={!run.reportAvailable || downloading} onClick={() => void download('report')}>下载完整报告 JSON</Button>
          <Button disabled={downloading} onClick={() => void download('evidence')}>下载规则与历史证据</Button>
          {evidence.data && <Button disabled={downloading} onClick={() => void download('manifest', evidence.data!.inputId)}>下载目标清单</Button>}
        </Space>
        {error && <Alert type="error" title="下载失败" description={error} />}
        {evidence.data && <details><summary className="cursor-pointer">已绑定的输入与历史基线（不可编辑）</summary>
          <p className="break-all">目标：{evidence.data.inputId}；对照：{evidence.data.comparisonInputId || '未选择'}</p>
          <Table size="small" rowKey="sourceId" pagination={false} scroll={{ x: 600 }} dataSource={evidence.data.historyBaseline.entries} columns={[
            { title: '序列', dataIndex: 'sequenceNumber' }, { title: '输入 ID', dataIndex: 'sourceId' }, { title: '信任状态', dataIndex: 'trustStatus' },
            { title: '输入摘要', dataIndex: 'contentDigest', render: value => <span className="break-all text-xs">{value}</span> },
            { title: '清单', render: (_, row) => <Button size="small" disabled={downloading} onClick={() => void download('manifest', row.sourceId)}>下载清单</Button> },
          ]} />
        </details>}
        {run.status === 'Completed' && <Tabs activeKey={tab} onChange={setTab} items={[
          { key: 'findings', label: '问题与定位', children: <>
            <Form form={form} layout="vertical" onFinish={values => { setFilters(values); setOffset(0) }}>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-3">
                <Form.Item name="severity" label="严重级别"><Select allowClear options={Object.entries(severityLabels).map(([value, label]) => ({ value, label }))} /></Form.Item>
                <Form.Item name="status" label="执行结果"><Select allowClear options={Object.entries(checkLabels).map(([value, label]) => ({ value, label }))} /></Form.Item>
                <Form.Item name="category" label="规则类别"><Select allowClear options={categories.map(value => ({ value, label: value }))} /></Form.Item>
                <Form.Item name="ruleId" label="规则 ID"><Select allowClear showSearch options={rules.map(rule => ({ value: rule.internalRuleId, label: `${rule.internalRuleId} · ${rule.title}` }))} /></Form.Item>
                <Form.Item name="nodePath" label="章节 / XML 节点路径包含"><Input placeholder="例如 m3-2-s-4-1；无节点定位的问题不会匹配" /></Form.Item>
                <Form.Item name="logicalPath" label="文件逻辑路径（精确）"><Input placeholder="0000/m2/overview.pdf" /></Form.Item>
              </div>
              <Space className="mb-3"><Button htmlType="submit" type="primary">筛选问题</Button><Button onClick={() => { form.resetFields(); setFilters({}); setOffset(0) }}>清除筛选</Button></Space>
            </Form>
            <Table size="small" loading={findings.isFetching} rowKey={(_, index) => `${offset + (index || 0)}`} scroll={{ x: 780 }} dataSource={findings.data?.items || []} columns={[
              { title: '规则 / 结果', width: 170, render: (_, row) => <>{row.ruleId}<br /><Tag color={row.checkStatus === 'Fail' ? 'red' : 'orange'}>{checkLabels[row.checkStatus]}</Tag>{severityLabels[row.severity]}</> },
              { title: '问题', render: (_, row) => <><strong>{row.code}</strong><p className="m-0">{row.message}</p><small className="break-all">{row.location.logicalPath || row.location.nodePath || '规则级问题'}</small></> },
              { title: '定位', width: 100, render: (_, row) => <Button size="small" onClick={() => setFinding(row)}>查看定位</Button> },
            ]} pagination={{ current: offset / 20 + 1, pageSize: 20, total: findings.data?.totalCount || 0, showSizeChanger: false,
              showTotal: total => `共 ${total} 项问题`, onChange: page => setOffset((page - 1) * 20) }} />
          </> },
          { key: 'coverage', label: '完整规则覆盖', children: <>
            {report.data && <Alert className="mb-3" type={report.data.isReadyForFinalization ? 'success' : 'warning'} title={report.data.isReadyForFinalization ? '该报告声明其范围内就绪' : '未满足正式定稿要求'} description={report.data.blockingReasons.join('；')} />}
            <Table size="small" loading={report.isFetching} rowKey="ruleId" dataSource={report.data?.coverage || []} scroll={{ x: 680 }} columns={[
              { title: '规则', dataIndex: 'ruleId' }, { title: '实现状态', dataIndex: 'implementationStatus' },
              { title: '执行结果', render: (_, row) => checkLabels[row.checkStatus] }, { title: '阻断', render: (_, row) => row.blocksReadiness ? '是' : '否' },
              { title: '检查说明', dataIndex: 'reason' },
            ]} />
            {!report.isFetching && !report.data && <Alert type="warning" showIcon className="mt-3" title="尚无归档报告"
              description="此运行没有可用报告或旧记录缺少覆盖统计；不会补写成功结论。" />}
          </> },
          { key: 'external', label: '外部对照证据', children: <ExternalComparisonPanel runId={id} packageDigest={run.binding.inputManifestDigest} /> },
        ]} />}
      </>}
    </Space>
    {finding && run && <FindingLocation finding={finding} evidence={evidence.data} sequence={run.sequenceNumber} onClose={() => setFinding(undefined)} />}
  </Card>
}
