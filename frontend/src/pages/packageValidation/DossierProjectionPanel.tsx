import { useState } from 'react'
import { Alert, Input, Radio, Space, Table, Tag } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { loadValidationProjection, type ProjectionLeaf, type ProjectionView } from '../../packageValidationActions'
import { checkLabels, projectionStatusLabels, projectionViewLabels } from './validationDisplay'

type Props = { runId: string; targetSequence: string }

const statusColor = (status: ProjectionLeaf['status']) => status === 'Current' || status === 'AppendMember'
  ? 'green' : status === 'Unknown' ? 'orange' : 'default'

// The cumulative view is archived evidence from the completed run. Every view is a
// page of that one document; nothing is re-derived from the mutable workspace.
export const DossierProjectionPanel = ({ runId, targetSequence }: Props) => {
  const [view, setView] = useState<ProjectionView>('current')
  const [sequenceNumber, setSequenceNumber] = useState(targetSequence)
  const [offset, setOffset] = useState(0)
  const projection = useQuery({
    queryKey: ['package-validation-projection', runId, view, sequenceNumber, offset],
    queryFn: ({ signal }) => loadValidationProjection(runId, view, offset, sequenceNumber, signal),
  })
  const data = projection.data
  return <Space orientation="vertical" size="middle" className="w-full">
    <Alert type="info" showIcon title="累计视图是归档推导证据，不是通过结论"
      description="视图来自该运行归档的同一份投影文档；无法解析的历史操作会保留为未解决并标记为不完整，不会被当作有效资料。" />
    <Space wrap>
      <Radio.Group value={view} onChange={event => { setView(event.target.value as ProjectionView); setOffset(0) }} optionType="button"
        options={Object.entries(projectionViewLabels).map(([value, label]) => ({ value, label }))} />
      {view === 'sequence' && <Input aria-label="序列号" addonBefore="序列" value={sequenceNumber} maxLength={4}
        onChange={event => { setSequenceNumber(event.target.value); setOffset(0) }} className="w-40" />}
    </Space>
    {projection.error && <Alert type="error" showIcon title="累计视图读取失败"
      description={projection.error instanceof Error ? projection.error.message : '未知错误'} />}
    {data && <>
      {!data.isComplete && <Alert type="warning" showIcon title="此累计视图不完整，不能作为定稿依据"
        description={data.incompleteReasons.length ? data.incompleteReasons.join('；') : '存在未解决的投影原因。'} />}
      <Space wrap size="small">
        <Tag>截至序列 {data.targetSequence}</Tag>
        <Tag>已应用 {data.appliedSequences.join('、') || '无'}</Tag>
        <Tag>历史基线 {data.baseline.length} 个序列</Tag>
        <Tag color={data.isComplete ? 'blue' : 'orange'}>{data.isComplete ? '完整' : '不完整'}</Tag>
      </Space>
      <Table<ProjectionLeaf> size="small" loading={projection.isFetching}
        rowKey={leaf => `${leaf.address.sequenceNumber}/${leaf.address.backboneRelativePath}#${leaf.address.leafId}`}
        scroll={{ x: 860 }} dataSource={data.leaves}
        pagination={{ current: offset / 50 + 1, pageSize: 50, total: data.totalCount, showSizeChanger: false,
          showTotal: total => `共 ${total} 个 leaf`, onChange: page => setOffset((page - 1) * 50) }}
        columns={[
          { title: '序列 / Backbone', width: 210, render: (_, leaf) => <span className="break-all">
            {leaf.address.sequenceNumber}/{leaf.address.backboneRelativePath}</span> },
          { title: 'Leaf', width: 150, render: (_, leaf) => <span className="break-all">{leaf.address.leafId}</span> },
          { title: '状态', width: 100, render: (_, leaf) => <Tag color={statusColor(leaf.status)}>
            {projectionStatusLabels[leaf.status]}</Tag> },
          { title: '操作 / 关系', width: 180, render: (_, leaf) => <span className="break-all text-xs">
            {leaf.operation || '—'}
            {leaf.replacedBy && <><br />被 {leaf.replacedBy} 替换</>}
            {leaf.deletedBy && <><br />被 {leaf.deletedBy} 删除</>}
            {leaf.appendedTo && <><br />追加至 {leaf.appendedTo}</>}
          </span> },
          { title: '内容', render: (_, leaf) => <span className="break-all">{leaf.contentLogicalPath || '无交付内容'}
            {leaf.isRegionalReference && <Tag className="ml-2">区域引用</Tag>}</span> },
        ]} />
      {view === 'history' && <Table size="small" rowKey={event => `${event.source.sequenceNumber}/${event.source.backboneRelativePath}#${event.source.leafId}:${event.operation}`}
        title={() => '生命周期事件（未解决事件保留原因）'} scroll={{ x: 760 }} dataSource={data.events} pagination={{ pageSize: 10, showSizeChanger: false }}
        columns={[
          { title: '源 leaf', render: (_, event) => <span className="break-all">
            {event.source.sequenceNumber}/{event.source.backboneRelativePath}#{event.source.leafId}</span> },
          { title: '操作', dataIndex: 'operation', width: 90 },
          { title: '目标 leaf', render: (_, event) => event.target
            ? <span className="break-all">{event.target.sequenceNumber}/{event.target.backboneRelativePath}#{event.target.leafId}</span>
            : '—' },
          { title: '结果', width: 100, render: (_, event) => checkLabels[event.status] },
          { title: '未解决原因', render: (_, event) => event.unresolvedReason || '—' },
        ]} />}
    </>}
  </Space>
}
