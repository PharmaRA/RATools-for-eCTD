import { useEffect, useState } from 'react'
import { Alert, Button, Card, Form, Input, InputNumber, Modal, Select, Space, Tag, message } from 'antd'
import { apiFetch, buildJsonRequestInit } from '../../apiClient'
import type { CtdNodeClonePreview, CtdNodeTreeDto, UpdateCtdNodeRequest } from '../../api/generated'
import type { WorkspaceTreeNode } from '../../workspaceTree'
import { requireWorkspaceRevision } from '../../workspaceActions'
import { getErrorMessage } from '../appShared'
import { nodeContext } from './nodeTree'

const attributeLabels: Record<string, string> = {
  substance: '原料药', manufacturer: '生产商', 'product-name': '制剂名称', dosageform: '剂型', indication: '适应症',
  'xml:lang': '语言', ID: 'XML 标识',
}

type Props = {
  tree: CtdNodeTreeDto
  selected: Extract<WorkspaceTreeNode, { nodeType: 'section' }>
  disabled: boolean
  onChanged: (tree: CtdNodeTreeDto) => Promise<void>
}

export const NodePropertiesPanel = ({ tree, selected, disabled, onChanged }: Props) => {
  const [form] = Form.useForm()
  const [cloning, setCloning] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<{ value: CtdNodeClonePreview, request: UpdateCtdNodeRequest } | null>(null)
  const node = tree.nodes.find(item => item.nodeInstanceId === selected.nodeInstanceId)
  const definition = tree.definitions.find(item => item.definitionKey === selected.definitionKey)
  const route = `/api/applications/${tree.applicationId}/sequences/${tree.sequenceNumber}/nodes`

  useEffect(() => {
    form.resetFields()
    form.setFieldsValue({ attributes: node?.attributes ?? {}, title: node?.title ?? '', sortOrder: node?.sortOrder ?? 0, storageSegment: node?.storageSegment ?? '' })
    setCloning(false)
    setPreview(null)
    setError(null)
  }, [form, node, selected.key])

  if (!definition) return null

  const execute = async (action: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try { await action() } catch (failure) { setError(getErrorMessage(failure)) } finally { setBusy(false) }
  }
  const values = async (): Promise<UpdateCtdNodeRequest> => {
    const fields = await form.validateFields()
    return { expectedRevision: requireWorkspaceRevision(tree.workspaceRevision), title: fields.title || null, sortOrder: fields.sortOrder ?? 0,
      storageSegment: fields.storageSegment || null,
      attributes: Object.fromEntries(Object.entries(fields.attributes ?? {}).filter(([, value]) => value !== '' && value != null)) as Record<string, string> }
  }
  const save = () => execute(async () => {
    const request = await values()
    if (cloning && node) {
      const value = await apiFetch(`${route}/${node.nodeInstanceId}/clone/preview`, buildJsonRequestInit('POST', request)) as CtdNodeClonePreview
      setPreview({ value, request })
    } else {
      const updated = await apiFetch(node ? `${route}/${node.nodeInstanceId}` : route, buildJsonRequestInit(node ? 'PUT' : 'POST',
        node ? request : { ...request, definitionKey: definition.definitionKey, parentInstanceId: selected.parentInstanceId })) as CtdNodeTreeDto
      await onChanged(updated)
      message.success(node ? '节点属性已保存' : '节点已创建')
    }
  })

  return <Card title={cloning ? '复制业务分组' : node ? '节点属性' : '创建章节实例'} size="small" className="mb-3">
    {node && <Space wrap className="mb-3"><Tag>{node.metadataStatus === 'Complete' ? '属性完整' : '待补全或映射'}</Tag><span>{node.ctdSection.toUpperCase()} {nodeContext(tree, node.nodeInstanceId)}</span></Space>}
    {node?.issues.map(issue => <Alert key={issue.fieldPath} type="warning" title={issue.message} className="mb-2" />)}
    {node && !cloning && <p className="text-xs text-gray-500">生产商等身份属性与历史序列关联。需要更改身份时，请复制为新业务分组，再明确映射文件。</p>}
    {error && <Alert type="error" showIcon title={error} className="mb-3" />}
    <Form form={form} layout="vertical" disabled={disabled || busy} onFinish={() => void save()}>
      {definition.attributes.map(attribute => <Form.Item key={attribute.name} name={['attributes', attribute.name]}
        label={`${attributeLabels[attribute.name] ?? attribute.name}${attribute.identity ? '（业务身份）' : ''}`}
        rules={[{ required: attribute.required, whitespace: true, message: '请填写此必填属性' }]}>
        {attribute.allowedValues.length ? <Select allowClear disabled={!!node && !cloning && attribute.identity} options={attribute.allowedValues.map(value => ({ label: value, value }))} />
          : <Input disabled={!!node && !cloning && attribute.identity} />}
      </Form.Item>)}
      <Form.Item name="title" label="显示标题" rules={[{ required: definition.kind === 1, whitespace: true, message: '扩展分组需要标题' }]}><Input maxLength={512} /></Form.Item>
      <Form.Item name="sortOrder" label="排序位置" extra="数字较小的同级实例或文件排在前面；规范规定的章节顺序保持固定。"><InputNumber min={0} precision={0} /></Form.Item>
      {definition.repeatable && <Form.Item name="storageSegment" label="目录标识" extra="使用小写字母、数字和连字符；保存后保持稳定。"
        rules={[{ required: true, pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/, max: 64, message: '请输入合法目录标识，例如 drug-a-alpha' }]}>
        <Input disabled={!!node?.storageSegment && !cloning} maxLength={64} />
      </Form.Item>}
      <Space wrap>
        <Button type="primary" htmlType="submit" loading={busy}>{cloning ? '预览复制' : node ? '保存节点' : '创建实例'}</Button>
        {node && definition.repeatable && !cloning && <Button onClick={() => { setCloning(true); setError(null); form.setFieldValue('storageSegment', '') }}>复制结构</Button>}
        {node && !cloning && <Button danger onClick={() => void execute(async () => {
          const result = await apiFetch(`${route}/${node.nodeInstanceId}?expectedRevision=${requireWorkspaceRevision(tree.workspaceRevision)}`, { method: 'DELETE' }) as CtdNodeTreeDto
          await onChanged(result)
        })}>移除空节点</Button>}
      </Space>
    </Form>
    <Modal title="复制结构预览" open={!!preview} onCancel={() => setPreview(null)} confirmLoading={busy} okText="创建新业务分组" onOk={() => void execute(async () => {
      if (!preview || !node) return
      const result = await apiFetch(`${route}/${node.nodeInstanceId}/clone`, buildJsonRequestInit('POST', preview.request)) as CtdNodeTreeDto
      setPreview(null)
      await onChanged(result)
    })}>
      <p>将创建 {preview?.value.nodes.length} 个节点。新分组为空，文件可随后上传或明确移动。</p>
      <ul>{Object.values(preview?.value.directories ?? {}).map(path => <li key={path} className="break-all">{path}</li>)}</ul>
    </Modal>
  </Card>
}

export const InheritNodeStructure = ({ tree, onChanged, disabled }: { tree: CtdNodeTreeDto, disabled: boolean, onChanged: Props['onChanged'] }) => {
  const [sequence, setSequence] = useState(String(Math.max(0, Number(tree.sequenceNumber) - 1)).padStart(4, '0'))
  const [busy, setBusy] = useState(false)
  return tree.sequenceNumber === '0000' ? null : <Space className="mb-3">
    <Input aria-label="历史结构来源序列" value={sequence} maxLength={4} onChange={event => setSequence(event.target.value)} style={{ width: 90 }} />
    <Button disabled={disabled || !/^\d{4}$/.test(sequence) || sequence >= tree.sequenceNumber} loading={busy} onClick={async () => {
      setBusy(true)
      try {
        const result = await apiFetch(`/api/applications/${tree.applicationId}/sequences/${tree.sequenceNumber}/nodes/inherit`, buildJsonRequestInit('POST',
          { sourceSequenceNumber: sequence, expectedRevision: requireWorkspaceRevision(tree.workspaceRevision) })) as CtdNodeTreeDto
        await onChanged(result)
      } catch (failure) { message.error(getErrorMessage(failure)) } finally { setBusy(false) }
    }}>沿用历史结构</Button>
  </Space>
}
