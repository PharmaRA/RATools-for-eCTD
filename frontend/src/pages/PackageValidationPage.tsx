import { useState } from 'react'
import { Alert, Button, Card, Input, Space, Table } from 'antd'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { isValidationActive, loadValidationProfiles, loadValidationRuns } from '../packageValidationActions'
import { ValidationRequestForm } from './packageValidation/ValidationRequestForm'
import { ValidationRunDetail } from './packageValidation/ValidationRunDetail'
import { runStatusLabels } from './packageValidation/validationDisplay'

export const PackageValidationPage = () => {
  const [params, setParams] = useSearchParams()
  const selectedId = params.get('run')
  const [offset, setOffset] = useState(0)
  const [application, setApplication] = useState<string>()
  const queryClient = useQueryClient()
  const profiles = useQuery({ queryKey: ['package-validation-profiles'], queryFn: ({ signal }) => loadValidationProfiles(signal) })
  const runs = useQuery({ queryKey: ['package-validation-runs', offset, application], queryFn: ({ signal }) => loadValidationRuns(offset, application, signal),
    refetchInterval: query => query.state.error ? false : query.state.data?.items.some(isValidationActive) ? 2000 : false,
  })
  return <div className="space-y-4">
    <div><h2 className="text-2xl font-bold mb-1">验证已有申报包</h2><p className="text-slate-500">从真实目录或 ZIP 独立读取交付物；不依赖本项目生成器或业务文档记录。</p></div>
    {profiles.error && <Alert type="error" title="规则范围加载失败" description={profiles.error.message} action={<Button onClick={() => void profiles.refetch()}>重试</Button>} />}
    <ValidationRequestForm profiles={profiles.data || []} onCreated={run => {
      queryClient.setQueryData(['package-validation-run', run.id], run)
      void queryClient.invalidateQueries({ queryKey: ['package-validation-runs'] })
      setParams({ run: run.id })
    }} />
    <Card size="small" title="独立验包历史" extra={<Button onClick={() => void runs.refetch()}>刷新历史</Button>}>
      <Space wrap className="mb-3"><Input.Search aria-label="按申请标识筛选历史" placeholder="按申请 UUID 筛选；留空显示全部" allowClear onSearch={value => { setApplication(value.trim() || undefined); setOffset(0) }} /></Space>
      {runs.error && <Alert type="error" title="历史加载失败" description={runs.error.message} />}
      <Table size="small" loading={runs.isFetching} rowKey="id" dataSource={runs.data?.items || []} scroll={{ x: 820 }} columns={[
        { title: '创建时间', dataIndex: 'createdUtc' }, { title: '序列', dataIndex: 'sequenceNumber' },
        { title: '运行状态', render: (_, row) => runStatusLabels[row.status] },
        { title: '覆盖', render: (_, row) => row.coverageSummary ? `失败 ${row.coverageSummary.failed} / 未验证 ${row.coverageSummary.notEvaluated}` : '尚无统计' },
        { title: '报告', render: (_, row) => <Link to={`?run=${encodeURIComponent(row.id)}`}>查看运行</Link> },
      ]} pagination={{ current: offset / 20 + 1, pageSize: 20, total: runs.data?.totalCount || 0, showSizeChanger: false, onChange: page => setOffset((page - 1) * 20) }} />
    </Card>
    {selectedId && <ValidationRunDetail key={selectedId} id={selectedId} />}
  </div>
}
